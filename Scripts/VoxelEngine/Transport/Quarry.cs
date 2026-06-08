using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Power;

namespace VoxelEngine.Transport
{
    public enum QuarryPhase { Idle, TapeFrame, BuildingFrame, Mining, Complete }

    [RequireComponent(typeof(PlacedBlock))]
    [RequireComponent(typeof(PortConfig))]
    [RequireComponent(typeof(ItemPortRouting))]
    public class Quarry : MonoBehaviour, IItemPortHost
    {
        [Header("Area")]
        public int defaultSize = 16;
        public float forwardOffset = 2f;

        [Header("Mining")]
        public float baseMineInterval = 0.5f;
        public int minePerTick = 1;
        [Range(0, 4)] public int quarryTier = 3;

        [Header("Power")]
        public float basePowerDraw = 500f;
        public float powerPerPerfUpgrade = 25f;
        public float powerSavePerEffUpgrade = 35f;

        [Header("Frame")]
        public float frameBuildInterval = 0.06f;
        public Color frameColor = new(0.18f, 0.19f, 0.22f);
        public Color frameAccentColor = new(0.92f, 0.52f, 0.08f, 0.85f);

        [Header("Tape")]
        public float tapePreviewDuration = 2.5f;
        public Color tapeColor1 = new(0.95f, 0.55f, 0.05f, 0.75f);
        public Color tapeColor2 = new(0.92f, 0.82f, 0.08f, 0.75f);

        [Header("Output")]
        public int outputSlots = 6;
        public const int UPGRADE_SLOTS = 3;
        public ItemContainer upgradeC;

        public QuarryPhase Phase { get; private set; } = QuarryPhase.Idle;
        public int CurrentDepth { get; private set; }
        public int MaxDepth { get; private set; }
        public bool IsMining => Phase == QuarryPhase.Mining;
        public float MineProgress01 => _mt / Mathf.Max(0.01f, EffInterval);
        public ItemContainer Output { get { EnsureOutput(); return _out; } }
        public int AreaX { get; private set; }
        public int AreaZ { get; private set; }
        public Vector3Int AreaMin { get; private set; }
        public Vector3Int AreaMax { get; private set; }
        public int CursorX => _cx;
        public int CursorZ => _cz;
        public bool IsOutputFull { get; private set; }

        public int InstalledRangeLevel { get; private set; }
        public int InstalledSpeedLevel { get; private set; }
        public int InstalledEfficiencyLevel { get; private set; }
        public const int MaxRangeLevel = 10;
        public const int MaxSpeedLevel = 10;
        public const int MaxEfficiencyLevel = 5;

        public int EffSize => defaultSize + InstalledRangeLevel;
        public float EffInterval => Mathf.Max(0.05f, baseMineInterval - InstalledSpeedLevel * 0.04f);
        public float EffPowerDraw => Mathf.Max(10f,
            basePowerDraw + (InstalledRangeLevel + InstalledSpeedLevel) * powerPerPerfUpgrade
                          - InstalledEfficiencyLevel * powerSavePerEffUpgrade);

        private float _mbY = -999f, _mtY = -999f;
        private ItemContainer _out;
        private float _mt, _fbt, _tt;
        private int _cx, _cz, _ls;
        private VoxelWorld _w;
        private MaterialRegistry _mr;
        private PowerConsumer _pc;
        private bool _ok;
        private Vector3Int _org;

        // Ghost persists from placement until power is applied
        private GameObject _ghost;
        private GameObject _ta, _dr, _bm;
        private List<GameObject> _fs = new();
        private int _fi;
        private List<FS> _fp = new();
        private float _tft;
        private bool _tfs;
        private struct FS { public Vector3 p, s; public bool a; }

        void Awake() { EnsureOutput(); EnsureUpgrades(); }
        void Start()
        {
            _w = VoxelWorld.Instance; _mr = _w?.materialRegistry;
            _pc = GetComponent<PowerConsumer>();
            if (_w == null) { enabled = false; return; }
            _org = _w.WorldToVoxel(transform.position);
            _mbY = transform.position.y - 1.2f;
            _mtY = transform.position.y + 1.2f;
            RecomputeArea(); _ls = EffSize; CalculateMaxDepth();
            RegisterUpgradeListener();
            StartCoroutine(CaptureModelBounds());
            _ok = true;
            // Show ghost on placement (before power)
            CreateGhost();
        }

        IEnumerator CaptureModelBounds()
        {
            yield return null;
            var col = GetComponentInChildren<BoxCollider>(true);
            if (col != null) { _mbY = col.bounds.min.y; _mtY = col.bounds.max.y; yield break; }
            var r = GetComponentInChildren<MeshRenderer>(true);
            if (r != null) { _mbY = r.bounds.min.y; _mtY = r.bounds.max.y; yield break; }
            var anyCol = GetComponentInChildren<Collider>(true);
            if (anyCol != null) { _mbY = anyCol.bounds.min.y; _mtY = anyCol.bounds.max.y; }
        }

        void Update()
        {
            if (!_ok) return;
            if (_pc) _pc.wattsPerSecond = EffPowerDraw;
            bool hp = _pc == null || _pc.IsPowered;

            // Destroy ghost when power comes on (real frame is about to build)
            if (hp && _ghost != null) { DestroyGhost(); }

            if (EffSize != _ls) { _ls = EffSize; RecomputeArea(); CalculateMaxDepth();
                if (Phase == QuarryPhase.Mining) { DA(); _cx=_cz=0; CurrentDepth=0; Phase=QuarryPhase.Idle; } }

            switch (Phase)
            {
                case QuarryPhase.Idle: if (hp) ETP(); break;
                case QuarryPhase.TapeFrame: if (!hp) return; _tt += Time.deltaTime; TkT(); if (_tt>=tapePreviewDuration){DTp();EFP();} break;
                case QuarryPhase.BuildingFrame: if (!hp) return; _fbt += Time.deltaTime; if (_fbt>=frameBuildInterval){_fbt=0f; if(_fi<_fp.Count)PS(_fp[_fi++]); else{Phase=QuarryPhase.Mining;CDr();}} break;
                case QuarryPhase.Mining: if (!hp) return; _mt += Time.deltaTime; if (_mt>=EffInterval){_mt-=EffInterval; for(int i=0;i<minePerTick;i++)MO();} TkD(); break;
                case QuarryPhase.Complete: if(_dr){Destroy(_dr);_dr=null;} if(_bm){Destroy(_bm);_bm=null;} break;
            }
        }

        void DA() { DTp(); foreach(var fb in _fs)if(fb)Destroy(fb); _fs.Clear();_fp.Clear(); if(_dr){Destroy(_dr);_dr=null;} if(_bm){Destroy(_bm);_bm=null;} }
        void ETP() { Phase=QuarryPhase.TapeFrame; _tt=0f; CTp(); }
        void EFP() { Phase=QuarryPhase.BuildingFrame; PF(); _fi=0; _fbt=0f; }

        Vector3Int FD() { var f=transform.forward; float ax=Mathf.Abs(f.x),az=Mathf.Abs(f.z); return ax>=az?new(Mathf.RoundToInt(Mathf.Sign(f.x)),0,0):new(0,0,Mathf.RoundToInt(Mathf.Sign(f.z))); }
        void RecomputeArea() { var fwd=FD(); if(fwd==default)fwd=new(1,0,0); int sy=_org.y,sz=EffSize; var perp=new Vector3Int(fwd.z,0,-fwd.x); int h=sz/2; var s=_org+fwd*(int)forwardOffset-perp*h; var e=s+fwd*sz+perp*sz; AreaMin=new(Mathf.Min(s.x,e.x),sy,Mathf.Min(s.z,e.z)); AreaMax=new(Mathf.Max(s.x,e.x)-1,sy,Mathf.Max(s.z,e.z)-1); AreaX=Mathf.Abs(AreaMax.x-AreaMin.x)+1; AreaZ=Mathf.Abs(AreaMax.z-AreaMin.z)+1; }
        void CalculateMaxDepth() { MaxDepth=Mathf.Max(1,AreaMin.y-3); }

        public void EnsureUpgrades() { if(upgradeC==null){upgradeC=new("Upgrades",UPGRADE_SLOTS);upgradeC.OnChanged+=RU;} else upgradeC.Resize(UPGRADE_SLOTS); }
        void RegisterUpgradeListener() { EnsureUpgrades(); upgradeC.OnChanged-=RU; upgradeC.OnChanged+=RU; RU(); }
        void RU() { InstalledRangeLevel=InstalledSpeedLevel=InstalledEfficiencyLevel=0; if(upgradeC==null)return; for(int i=0;i<upgradeC.Size;i++){var st=upgradeC.GetSlot(i); if(st.IsEmpty||!(st.item is QuarryUpgradeItem u))continue; int a=u.level*st.count; switch(u.upgradeKind){case QuarryUpgradeKind.Range:InstalledRangeLevel=Mathf.Min(MaxRangeLevel,a);break;case QuarryUpgradeKind.Speed:InstalledSpeedLevel=Mathf.Min(MaxSpeedLevel,a);break;case QuarryUpgradeKind.Efficiency:InstalledEfficiencyLevel=Mathf.Min(MaxEfficiencyLevel,a);break;}}}
        public bool TryInstallUpgrade(QuarryUpgradeItem item) { if(item==null)return false; EnsureUpgrades(); return upgradeC.Insert(new(item,1)).IsEmpty; }

        // ═══ PLACEMENT PREVIEW (BuildSystem) ═══
        static GameObject _pp;
        public static void ShowPlacementPreview(Vector3 wp, Quaternion rot, int sz, float fo)
        {
            HPP(); _pp=new("QPP");
            var vox=VoxelWorld.Instance?.WorldToVoxel(wp)??Vector3Int.zero;
            var f=rot*Vector3.forward; float ax=Mathf.Abs(f.x),az=Mathf.Abs(f.z);
            var d=ax>=az?new Vector3Int(Mathf.RoundToInt(Mathf.Sign(f.x)),0,0):new(0,0,Mathf.RoundToInt(Mathf.Sign(f.z)));
            if(d==default)d=new(1,0,0);
            var p=new Vector3Int(d.z,0,-d.x); int h=sz/2;
            var s=vox+d*(int)fo-p*h; var e=s+d*sz+p*sz;
            var amin=new Vector3Int(Mathf.Min(s.x,e.x),vox.y,Mathf.Min(s.z,e.z));
            var amax=new Vector3Int(Mathf.Max(s.x,e.x)-1,vox.y,Mathf.Max(s.z,e.z)-1);
            float yTop=vox.y+0.5f;           // surface
            float yBot=wp.y-1.2f;            // model bottom
            Color gc=new(1f,0.8f,0.2f,0.55f);
            var v0=V3(amin.x,yTop,amin.z);var v1=V3(amax.x+1,yTop,amin.z);
            var v2=V3(amax.x+1,yTop,amax.z+1);var v3=V3(amin.x,yTop,amax.z+1);
            AL(v0,v1,gc);AL(v1,v2,gc);AL(v2,v3,gc);AL(v3,v0,gc);
            AL(v0,V3(v0.x,yBot,v0.z),gc);AL(v1,V3(v1.x,yBot,v1.z),gc);
            AL(v2,V3(v2.x,yBot,v2.z),gc);AL(v3,V3(v3.x,yBot,v3.z),gc);
            var b0=V3(amin.x,yBot,amin.z);var b1=V3(amax.x+1,yBot,amin.z);
            var b2=V3(amax.x+1,yBot,amax.z+1);var b3=V3(amin.x,yBot,amax.z+1);
            AL(b0,b1,gc);AL(b1,b2,gc);AL(b2,b3,gc);AL(b3,b0,gc);
        }
        public static void HidePlacementPreview()=>HPP();
        static void HPP(){if(_pp){Object.Destroy(_pp);_pp=null;}}
        static void AL(Vector3 a,Vector3 b,Color c){var go=new GameObject("L");go.transform.SetParent(_pp.transform,false);var lr=go.AddComponent<LineRenderer>();lr.positionCount=2;lr.SetPositions(new[]{a,b});lr.startWidth=lr.endWidth=0.06f;lr.startColor=lr.endColor=c;lr.useWorldSpace=true;var sh=Shader.Find("Universal Render Pipeline/Particles/Unlit")??Shader.Find("Sprites/Default");lr.material=new Material(sh){color=c};lr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
        static Vector3 V3(float x,float y,float z)=>new(x,y,z);

        // ═══ GHOST (persists on placed quarry until powered) ═══
        void CreateGhost()
        {
            if (_ghost) Destroy(_ghost); _ghost=new("QuarryGhost");
            float yTop=_mtY; // model top
            float yBot=_mbY; // model bottom
            Color gc=new(1f,0.8f,0.2f,0.5f);
            var v0=V3(AreaMin.x,yTop,AreaMin.z);var v1=V3(AreaMax.x+1,yTop,AreaMin.z);
            var v2=V3(AreaMax.x+1,yTop,AreaMax.z+1);var v3=V3(AreaMin.x,yTop,AreaMax.z+1);
            GL(v0,v1,gc);GL(v1,v2,gc);GL(v2,v3,gc);GL(v3,v0,gc);
            GL(v0,V3(v0.x,yBot,v0.z),gc);GL(v1,V3(v1.x,yBot,v1.z),gc);
            GL(v2,V3(v2.x,yBot,v2.z),gc);GL(v3,V3(v3.x,yBot,v3.z),gc);
            var b0=V3(AreaMin.x,yBot,AreaMin.z);var b1=V3(AreaMax.x+1,yBot,AreaMin.z);
            var b2=V3(AreaMax.x+1,yBot,AreaMax.z+1);var b3=V3(AreaMin.x,yBot,AreaMax.z+1);
            GL(b0,b1,gc);GL(b1,b2,gc);GL(b2,b3,gc);GL(b3,b0,gc);
        }
        void GL(Vector3 a,Vector3 b,Color c){var go=new GameObject("L");go.transform.SetParent(_ghost.transform,false);var lr=go.AddComponent<LineRenderer>();lr.positionCount=2;lr.SetPositions(new[]{a,b});lr.startWidth=lr.endWidth=0.06f;lr.startColor=lr.endColor=c;lr.useWorldSpace=true;var sh=Shader.Find("Universal Render Pipeline/Particles/Unlit")??Shader.Find("Sprites/Default");lr.material=new Material(sh){color=c};lr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;}
        void DestroyGhost() { if (_ghost) { Destroy(_ghost); _ghost = null; } }

        // ═══ TAPE ════════════════════════════════════
        void CTp(){_ta=new("QT");float y=AreaMin.y+0.1f;var v0=V3(AreaMin.x,y,AreaMin.z);var v1=V3(AreaMax.x+1,y,AreaMin.z);var v2=V3(AreaMax.x+1,y,AreaMax.z+1);var v3=V3(AreaMin.x,y,AreaMax.z+1);TE(v0,v1);TE(v1,v2);TE(v2,v3);TE(v3,v0);float ph=2.5f;TP(v0,ph);TP(v1,ph);TP(v2,ph);TP(v3,ph);}
        void TE(Vector3 f,Vector3 t){var d=(t-f).normalized;float l=Vector3.Distance(f,t);int sc=Mathf.CeilToInt(l/0.5f);float sl=l/sc;for(int i=0;i<sc;i++){var p=f+d*(i*sl+sl*0.5f);var g=MK("T",p,_ta.transform);if(Mathf.Abs(d.x)>0.5f)g.transform.localScale=new(sl*0.95f,0.04f,0.08f);else g.transform.localScale=new(0.08f,0.04f,sl*0.95f);g.GetComponent<MeshRenderer>().material=ME((i%2)==0?tapeColor1:tapeColor2,0.6f);}}
        void TP(Vector3 bp,float h){var g=MK("P",bp+Vector3.up*h*0.5f,_ta.transform);g.transform.localScale=new(0.1f,h,0.1f);g.GetComponent<MeshRenderer>().material=ME(tapeColor1,0.8f);var o=MK("O",bp+Vector3.up*h,_ta.transform);o.transform.localScale=Vector3.one*0.2f;o.GetComponent<MeshRenderer>().material=ME(new(0.95f,0.55f,0.05f,0.9f),0.9f);}
        static GameObject MK(string n,Vector3 p,Transform pt){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=n;g.transform.SetParent(pt,false);g.transform.position=p;Object.Destroy(g.GetComponent<Collider>());return g;}
        void TkT(){if(!_ta)return;_tft+=Time.deltaTime;if(_tft>0.15f){_tft=0f;_tfs=!_tfs;float a=_tfs?1f:0.5f;foreach(var mr in _ta.GetComponentsInChildren<MeshRenderer>()){var c=mr.material.color;c.a=a*0.75f;mr.material.color=c;}}}
        void DTp(){if(_ta){Destroy(_ta);_ta=null;}}

        // ═══ 3D FRAME (model-top to model-bottom = 16×16×2.4) ═══
        void PF()
        {
            _fp.Clear();
            float yTop=_mtY;       // model top (= quarry block top)
            float yBot=_mbY;       // model bottom (= quarry block bottom)
            var v00=V3(AreaMin.x,0,AreaMin.z);var v10=V3(AreaMax.x+1,0,AreaMin.z);
            var v11=V3(AreaMax.x+1,0,AreaMax.z+1);var v01=V3(AreaMin.x,0,AreaMax.z+1);
            float pH=yTop-yBot+0.3f,pCY=(yTop+yBot)*0.5f;
            Pi(v00,pCY,pH);Pi(v10,pCY,pH);Pi(v11,pCY,pH);Pi(v01,pCY,pH);
            Be(v00,v10,yTop);Be(v10,v11,yTop);Be(v11,v01,yTop);Be(v01,v00,yTop);
            Be(v00,v10,yBot);Be(v10,v11,yBot);Be(v11,v01,yBot);Be(v01,v00,yBot);
            Ac(v00,yTop);Ac(v10,yTop);Ac(v11,yTop);Ac(v01,yTop);
        }
        void Pi(Vector3 p,float cy,float h)=>_fp.Add(new FS{p=new Vector3(p.x,cy,p.z),s=new Vector3(0.16f,h,0.16f),a=false});
        void Be(Vector3 f,Vector3 t,float y){var m=(f+t)*0.5f;float l=Vector3.Distance(f,t);_fp.Add(new FS{p=new Vector3(m.x,y,m.z),s=(Mathf.Abs(f.x-t.x)>0.01f?new Vector3(l,0.12f,0.1f):new Vector3(0.1f,0.12f,l)),a=false});}
        void Ac(Vector3 p,float y)=>_fp.Add(new FS{p=new Vector3(p.x,y,p.z),s=new Vector3(0.22f,0.22f,0.22f),a=true});
        void PS(FS s){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=s.a?"A":"F";go.transform.position=s.p;go.transform.localScale=s.s;var r=go.GetComponent<MeshRenderer>();if(s.a)r.material=ME(frameAccentColor,0.7f);else{var sh=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(sh){color=frameColor};m.SetColor("_BaseColor",frameColor);m.SetFloat("_Metallic",0.9f);m.SetFloat("_Smoothness",0.45f);r.material=m;}Destroy(go.GetComponent<BoxCollider>());_fs.Add(go);}

        // ═══ DRILL ═══════════════════════════════════
        void CDr(){_dr=GameObject.CreatePrimitive(PrimitiveType.Cylinder);_dr.name="H";_dr.transform.localScale=new(0.5f,0.25f,0.5f);var c=_dr.GetComponent<CapsuleCollider>();if(c)Destroy(c);var sh=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(sh);m.color=new(0.85f,0.55f,0.08f);m.SetColor("_BaseColor",m.color);m.SetFloat("_Metallic",0.8f);m.SetFloat("_Smoothness",0.5f);m.SetColor("_EmissionColor",new Color(0.85f,0.45f,0.05f)*0.4f);m.EnableKeyword("_EMISSION");_dr.GetComponent<MeshRenderer>().material=m;_bm=GameObject.CreatePrimitive(PrimitiveType.Cylinder);_bm.name="B";_bm.transform.localScale=new(0.08f,0.5f,0.08f);var bc=_bm.GetComponent<CapsuleCollider>();if(bc)Destroy(bc);var bmMat=new Material(sh);bmMat.color=new(1f,0.55f,0.05f,0.6f);bmMat.SetColor("_BaseColor",bmMat.color);bmMat.SetColor("_EmissionColor",new Color(1f,0.5f,0f)*0.8f);bmMat.EnableKeyword("_EMISSION");_bm.GetComponent<MeshRenderer>().material=bmMat;}
        void TkD(){if(!_dr)return;float dy=AreaMin.y-CurrentDepth-0.5f;var tp=new Vector3(AreaMin.x+_cx+0.5f,dy,AreaMin.z+_cz+0.5f);_dr.transform.position=tp;_dr.transform.Rotate(Vector3.up,220f*Time.deltaTime);if(_bm){_bm.transform.position=tp+Vector3.down*0.35f;_bm.transform.Rotate(Vector3.up,-180f*Time.deltaTime);}}

        // ═══ MINING ══════════════════════════════════
        void MO(){if(CurrentDepth>=MaxDepth){Phase=QuarryPhase.Complete;if(_dr)Destroy(_dr);if(_bm)Destroy(_bm);_dr=_bm=null;return;}EnsureOutput();bool sp=false;for(int i=0;i<_out.Size;i++)if(_out.GetSlot(i).IsEmpty){sp=true;break;}if(!sp){var hs=Physics.OverlapSphere(transform.position,1.6f);foreach(var c in hs){if(c.gameObject==gameObject)continue;var p=c.GetComponent<ItemPipe>();if(p&&p.GetInputCapacity(null)>0){sp=true;break;}}}if(!sp){IsOutputFull=true;return;}IsOutputFull=false;var t=new Vector3Int(AreaMin.x+_cx,AreaMin.y-CurrentDepth,AreaMin.z+_cz);var v=_w.GetVoxelWorld(t);if(v.material==(byte)MaterialId.Bedrock){AD();return;}if(v.density>VoxelConstants.ISO_LEVEL){var def=_mr?.Get(v.material);if(def==null||!def.isMineable||quarryTier>=def.miningTier){_w.SetVoxelWorld(t,Voxel.Empty);if(def?.dropItem&&def.dropAmount>0)OI(def.dropItem,def.dropAmount);}}AD();}
        void AD(){_cx++;if(_cx>=AreaX){_cx=0;_cz++;if(_cz>=AreaZ){_cz=0;CurrentDepth++;}}}
        void OI(ItemDefinition it,int n){int rem=n;var hs=Physics.OverlapSphere(transform.position,1.6f);foreach(var c in hs){if(c.gameObject==gameObject)continue;var p=c.GetComponent<ItemPipe>();if(!p)continue;int a=p.TryInsert(it,Mathf.Min(p.GetInputCapacity(it),rem));rem-=a;if(rem<=0)return;}if(rem>0){EnsureOutput();_out.Insert(new(it,rem));}}
        void EnsureOutput(){if(_out==null)_out=new("Output",outputSlots);else _out.Resize(outputSlots);}
        public void EnsureOutputPublic()=>EnsureOutput();

        // ── IItemPortHost ───────────────────────────────────────────────────
        private PortConfig _portConfig;
        private ItemPortContainer[] _portContainers;

        public PortConfig PortConfig
        {
            get
            {
                if (_portConfig == null)
                {
                    _portConfig = GetComponent<PortConfig>();
                    if (_portConfig == null) _portConfig = gameObject.AddComponent<PortConfig>();
                    _portConfig.EnsureAllFaces();
                }
                return _portConfig;
            }
        }

        public IReadOnlyList<ItemPortContainer> GetPortContainers()
        {
            EnsureOutput();
            _portContainers ??= new ItemPortContainer[1];
            _portContainers[0] = new ItemPortContainer("Output", Output, canInput: false, canOutput: true);
            return _portContainers;
        }
        public void RestoreState(int d,int cx,int cz,int ph,int rl,int sl,int el){CurrentDepth=d;_cx=cx;_cz=cz;Phase=(QuarryPhase)ph;InstalledRangeLevel=rl;InstalledSpeedLevel=sl;InstalledEfficiencyLevel=el;if(Phase==QuarryPhase.Complete){if(_dr)Destroy(_dr);if(_bm)Destroy(_bm);}}
        void OnDestroy(){DestroyGhost();DTp();HPP();foreach(var fb in _fs)if(fb)Destroy(fb);_fs.Clear();if(_dr)Destroy(_dr);if(_bm)Destroy(_bm);}
        static Material ME(Color c,float es){var s=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");var m=new Material(s);m.color=c;m.SetColor("_BaseColor",c);m.SetColor("_EmissionColor",c*es);m.EnableKeyword("_EMISSION");return m;}
    }
}
