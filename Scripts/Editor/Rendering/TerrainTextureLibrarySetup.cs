#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Materials;
using VoxelEngine.Rendering;

namespace VoxelEngine.EditorTools
{
    /// <summary>Rebuilds setup-owned texture arrays without changing source images/materials.</summary>
    public static class TerrainTextureLibrarySetup
    {
        public const string Root = "Assets/VoxelEngineAssets/TerrainTextures";
        private const string Output = "Assets/Resources/VoxelEngineRuntime/TerrainTextureLibrary.asset";
        private const int Resolution = 512;
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            AssetDatabase.Refresh();
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var names = new List<string>(); var ids = new List<int>();
            foreach (MaterialId id in Enum.GetValues(typeof(MaterialId)))
            {
                if (id == MaterialId.Air || id == MaterialId.WaterVoxel || id == MaterialId.WaterLiquid
                    || id == MaterialId.CrudeOil || (int)id >= 28 || id == MaterialId.LegacySolidFloor) continue;
                string folder = Root + "/" + id;
                Directory.CreateDirectory(folder);
                if (Find(folder, id + "_Albedo") == null) continue;
                names.Add(id.ToString()); ids.Add((int)id);
            }
            AssetDatabase.Refresh();
            if (!SystemInfo.supports2DArrayTextures)
            { Debug.LogError("[TerrainTextures] Texture arrays are unsupported on this editor graphics device."); return; }
            var library = AssetDatabase.LoadAssetAtPath<TerrainTextureLibrary>(Output);
            if (library == null) { library = ScriptableObject.CreateInstance<TerrainTextureLibrary>(); AssetDatabase.CreateAsset(library, Output); }
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(Output))
                if (asset is Texture2DArray) UnityEngine.Object.DestroyImmediate(asset, true);
            int count = Mathf.Max(1, names.Count);
            library.albedo = new Texture2DArray(Resolution, Resolution, count, TextureFormat.RGBA32, true, false) { name="TerrainAlbedo", wrapMode=TextureWrapMode.Repeat };
            library.normal = new Texture2DArray(Resolution, Resolution, count, TextureFormat.RGBA32, true, true) { name="TerrainNormal", wrapMode=TextureWrapMode.Repeat };
            library.mask = new Texture2DArray(Resolution, Resolution, count, TextureFormat.RGBA32, true, true) { name="TerrainMask", wrapMode=TextureWrapMode.Repeat };
            library.entries = new Vector4[256];
            for(int slice=0;slice<count;slice++)
            {
                string name=slice<names.Count?names[slice]:"";
                string folder=Root+"/"+name;
                Texture2D albedo=Find(folder,name+"_Albedo"), normal=Find(folder,name+"_Normal"),
                    rough=Find(folder,name+"_Roughness"), metal=Find(folder,name+"_Metallic"), ao=Find(folder,name+"_AO");
                Color[] a=Read(albedo,Color.white,false), n=Read(normal,new Color(0.5f,0.5f,1f),true),
                    rr=Read(rough,new Color(0.8f,0.8f,0.8f),true), mm=Read(metal,Color.black,true), oo=Read(ao,Color.white,true);
                var mask=new Color[Resolution*Resolution];
                for(int i=0;i<mask.Length;i++)mask[i]=new Color(mm[i].r,oo[i].r,rr[i].r,1f);
                library.albedo.SetPixels(a,slice);library.normal.SetPixels(n,slice);library.mask.SetPixels(mask,slice);
                if(slice<names.Count)library.entries[ids[slice]]=new Vector4(slice+1,normal!=null?1:0,rough!=null||metal!=null||ao!=null?1:0,2f);
            }
            library.albedo.Apply();library.normal.Apply();library.mask.Apply();
            AssetDatabase.AddObjectToAsset(library.albedo,library);AssetDatabase.AddObjectToAsset(library.normal,library);AssetDatabase.AddObjectToAsset(library.mask,library);
            EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
            Debug.Log("[TerrainTextures] Built "+names.Count+" named PBR surfaces. Missing albedo keeps procedural fallback. Source: "+Root);
        }
        private static Texture2D Find(string folder,string name)
        {
            foreach(string ext in new[]{".png",".jpg",".jpeg",".tga",".tif",".tiff"})
            {var t=AssetDatabase.LoadAssetAtPath<Texture2D>(folder+"/"+name+ext);if(t!=null)return t;}
            return null;
        }
        private static Color[] Read(Texture2D source,Color fallback,bool linear)
        {
            if(source==null){var pixels=new Color[Resolution*Resolution];Array.Fill(pixels,fallback);return pixels;}
            // Blit uses a private raw-data importer view for scalar/tangent normal maps.
            string path=AssetDatabase.GetAssetPath(source);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer missing: " + path);
            var originalType=importer.textureType;bool originalSrgb=importer.sRGBTexture;
            try
            {
                importer.textureType=TextureImporterType.Default;importer.sRGBTexture=!linear;importer.SaveAndReimport();
                source=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var rt=RenderTexture.GetTemporary(Resolution,Resolution,0,RenderTextureFormat.ARGB32,linear?RenderTextureReadWrite.Linear:RenderTextureReadWrite.sRGB);
                var previous=RenderTexture.active;
                var copy=new Texture2D(Resolution,Resolution,TextureFormat.RGBA32,false,linear);
                try{Graphics.Blit(source,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,Resolution,Resolution),0,0);copy.Apply();return copy.GetPixels();}
                finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
            }
            finally{importer.textureType=originalType;importer.sRGBTexture=originalSrgb;importer.SaveAndReimport();}
        }
    }
}
#endif
