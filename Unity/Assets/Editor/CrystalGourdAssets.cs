using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class CrystalGourdAssets
{
    const string Folder = "Assets/CrystalGourd";
    static readonly string[] Models = { "crystal_flask", "crystal_flask_LOD1", "crystal_flask_LOD2" };
    const string Generated = Folder + "/Generated";
    const string PrefabPath = Generated + "/CrystalGourdArt.prefab";
    const string Output = "Build";
    static readonly Vector3 Mouth = new Vector3(0f,.405f,-.065f);
    static readonly Quaternion Upright = Quaternion.Euler(0f,90f,0f);
    const float Scale = .86f;

    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Generated)) AssetDatabase.CreateFolder(Folder,"Generated");
        Directory.CreateDirectory(Output);
        var crystal = Material("Frost-blue crystal", new Color(.99f,.97f,.96f), false);
        crystal.SetFloat("_CrystalSaturation", .50f);
        crystal.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Models/ATT Blue Crystal.png");
        if (crystal.mainTexture == null) throw new InvalidOperationException("Native blue crystal texture is missing");
        crystal.mainTextureOffset = new Vector2(10f / 256f, 10f / 256f);
        crystal.mainTextureScale = Vector2.one * (236f / 256f);
        var shell = new Material(crystal) { name = "Translucent crystal shell", renderQueue = 3050 };
        shell.SetOverrideTag("RenderType", "Transparent");
        shell.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        shell.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        shell.SetFloat("_ZWrite", 0);
        shell.SetFloat("_Cull", (float)CullMode.Back);
        shell.SetFloat("_Opacity", .68f);
        shell.SetFloat("_Specular", .22f);
        shell.SetFloat("_Shininess", 32f);
        shell = SaveAsset(shell, Generated + "/Translucent crystal shell.mat");
        var metal = Material("Dark steel frame", Color.white, false);
        metal.mainTexture = LoadTexture("MetalPanels_diffuse");
        metal.SetFloat("_Specular", .56f);
        metal.SetFloat("_Shininess", 12f);
        var cork = Material("Warm cork", Color.white, false);
        cork.mainTexture = LoadTexture("woodcutting_d");
        // The cork UVs occupy the cut-wood area of the atlas. Keep that
        // grain and lift its tone to match the stopper in the reference.
        cork.SetFloat("_TextureLift", .34f);
        cork.SetFloat("_CrystalSaturation", .67f);
        cork.SetFloat("_Specular", .12f);
        cork.SetFloat("_Shininess", 18f);
        var root = new GameObject("CrystalGourdArt");
        var body = Child(root.transform,"Body");
        var stopper = Child(root.transform,"Cork");
        var recipe = Child(root.transform,"Recipe");
        var collision = Child(root.transform,"Collision");
        var lods = new LOD[3];
        for (int i=0; i<Models.Length; i++)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Models/" + Models[i] + ".fbx");
            var nodes = source.GetComponentsInChildren<MeshFilter>(true);
            var detail = Child(body,"LOD"+i);
            var gems = nodes.Where(n=>n.name.StartsWith("crystal_gem",StringComparison.Ordinal)).OrderBy(n=>n.GetComponent<Renderer>().bounds.center.y).ToArray();
            if (gems.Length!=3) throw new InvalidOperationException("Expected three crystal chambers in "+source.name);
            for (int chamber=0; chamber<gems.Length; chamber++)
            {
                var mesh=Save(MakeMesh(new[]{gems[chamber]},Vector3.zero,true),"Crystal_"+i+"_"+chamber);
                Display(detail,"Chamber "+chamber,mesh,shell);
                if (i==0) Display(recipe,"Gem "+chamber,mesh,crystal);
            }
            var frame=nodes.Where(n=>!n.name.StartsWith("Cork",StringComparison.Ordinal)&&!n.name.StartsWith("crystal_gem",StringComparison.Ordinal)&&!n.name.StartsWith("crystal_liquid",StringComparison.Ordinal));
            var frameMesh=Save(MakeMesh(frame,Vector3.zero,false),"Frame_"+i);
            Display(detail,"Steel frame",frameMesh,metal);
            // SyncLib fingerprints the workbench mesh in saved recipe IDs.
            // Retain its original UVs while restoring the FBX UVs on the item.
            if(i==0)
            {
                var recipeFrame = Save(MakeMesh(frame,Vector3.zero,true,true),"Workbench Frame");
                Display(recipe,"Frame",recipeFrame,metal);
            }
            lods[i]=new LOD(new[]{.23f,.07f,.008f}[i],detail.GetComponentsInChildren<Renderer>());
        }
        var high=AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Models/" + Models[0] + ".fbx").GetComponentsInChildren<MeshFilter>(true);
        foreach (var part in high.Where(node => node.name == "Cap" || node.name == "handle" || node.name.StartsWith("leg_", StringComparison.Ordinal)))
        {
            var mesh = Save(MakeMesh(new[]{part}, Vector3.zero, false), "Collision " + part.name);
            Child(collision, part.name).gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        }
        if (collision.childCount != 5) throw new InvalidOperationException("Missing flask frame collision parts");
        collision.gameObject.SetActive(false);
        var lip=Point(Mouth);
        Display(stopper,"Cork mesh",Save(MakeMesh(high.Where(n=>n.name=="Cork"),lip,false),"Cork"),cork);
        var inner=Save(MakeMesh(high.Where(n=>n.name=="crystal_liquid_chambers"),Vector3.zero,false),"Liquid chambers");
        Child(root.transform,"Liquid").gameObject.AddComponent<MeshFilter>().sharedMesh=inner;
        var lod=body.gameObject.AddComponent<LODGroup>();
        lod.SetLODs(lods);lod.RecalculateBounds();
        recipe.gameObject.SetActive(false);
        var marker=Child(root.transform,"Mouth");marker.localPosition=lip;
        var axis=Child(root.transform,"Neck Direction");axis.localPosition=Point(new Vector3(0f,.384f,-.055f));
        var icon=GenerateIcon();
        var iconRenderer=Child(root.transform,"Recipe icon").gameObject.AddComponent<SpriteRenderer>();iconRenderer.sprite=icon;iconRenderer.enabled=false;
        AssetDatabase.SaveAssets();
        PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        BuildPipeline.BuildAssetBundles(Output,new[]{new AssetBundleBuild { assetBundleName="crystal-gourd-art",assetNames=new[]{PrefabPath} }},
            BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode,BuildTarget.StandaloneWindows64);
        if (!File.Exists(Path.Combine(Output,"crystal-gourd-art"))) throw new InvalidOperationException("Unity failed to build the gourd asset bundle");
        Object.DestroyImmediate(root);
        Debug.Log("CRYSTAL_GOURD_ART_PASS 3 imported LODs, 3 chambers, separate cork and workbench pieces");
    }

    static Vector3 Point(Vector3 p) => Upright*(p-Mouth)*Scale+new Vector3(0,.3422f,0);

    static Texture2D LoadTexture(string name)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Models/" + name + ".png");
        if (texture == null) throw new InvalidOperationException("Missing texture " + name);
        return texture;
    }

    static Mesh MakeMesh(IEnumerable<MeshFilter> sources,Vector3 origin,bool tint,bool projectSurface=false)
    {
        var positions=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color>();var uv=new List<Vector2>();var triangles=new List<int>();
        foreach(var source in sources)
        {
            var mesh=source.sharedMesh;var matrix=source.transform.localToWorldMatrix;
            var points=mesh.vertices;var originalNormals=mesh.normals;var originalUv=mesh.uv;
            var normalMatrix=matrix.inverse.transpose;
            int start=positions.Count;
            for(int i=0;i<points.Length;i++)
            {
                var point=Point(matrix.MultiplyPoint3x4(points[i]))-origin;
                positions.Add(point);
                var normal=(Upright*normalMatrix.MultiplyVector(originalNormals[i])).normalized;
                normals.Add(normal);
                if (projectSurface)
                {
                    var b = mesh.bounds;
                    var p = new Vector3(Mathf.InverseLerp(b.min.x,b.max.x,points[i].x),
                        Mathf.InverseLerp(b.min.y,b.max.y,points[i].y),
                        Mathf.InverseLerp(b.min.z,b.max.z,points[i].z));
                    uv.Add(Mathf.Abs(originalNormals[i].y) > .8f ? new Vector2(p.x,p.z)
                        : new Vector2(Mathf.Abs(originalNormals[i].x) > .68f ? p.z : p.x,p.y));
                }
                else uv.Add(originalUv.Length==points.Length?originalUv[i]:Vector2.zero);
                var shade=tint?Mathf.Lerp(.74f,1.09f,.5f+.5f*Vector3.Dot(normal,new Vector3(-.37f,.80f,.47f))):1f;
                colors.Add(new Color(shade,shade,shade,1));
            }
            var indices=mesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                triangles.Add(start+indices[i]);triangles.Add(start+indices[i+(matrix.determinant<0?2:1)]);triangles.Add(start+indices[i+(matrix.determinant<0?1:2)]);
            }
        }
        if (positions.Count==0) throw new InvalidOperationException("Missing gourd mesh");
        var result=new Mesh();result.SetVertices(positions);result.SetNormals(normals);result.SetUVs(0,uv);result.SetColors(colors);result.SetTriangles(triangles,0);result.RecalculateBounds();return result;
    }

    static Transform Child(Transform parent,string name)
    {var node=new GameObject(name).transform;node.SetParent(parent,false);return node;}

    static void Display(Transform parent,string name,Mesh mesh,Material material)
    {var child=Child(parent,name);child.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;child.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;}

    static Mesh Save(Mesh mesh,string name)
    {mesh.name=name;return SaveAsset(mesh,Generated+"/"+name+".asset");}

    static T SaveAsset<T>(T asset,string path) where T:Object
    {
        var existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing==null){AssetDatabase.CreateAsset(asset,path);return asset;}
        EditorUtility.CopySerialized(asset,existing);Object.DestroyImmediate(asset);return existing;
    }

    static Material Material(string name,Color color,bool vertexTint)
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/Shaders/PlayerArmorLit.shader");
        var material=new Material(shader){name=name,color=color,enableInstancing=true};
        material.SetFloat("_UseAtlas",0f);material.SetFloat("_UseVertexColor",vertexTint?1f:0f);
        return SaveAsset(material,Generated+"/"+name+".mat");
    }

    static Sprite GenerateIcon()
    {
        const int size=128;
        var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);
        var pixels=new Color32[size*size];
        var centers=new[]{new Vector2(46,31),new Vector2(37,63),new Vector2(65,91)};
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            var point=new Vector2(x,y);
            bool ink=centers.Any(c=>{var d=point-c;float edge=Mathf.Max(Mathf.Abs(d.x),Mathf.Abs(d.y))+.36f*Mathf.Min(Mathf.Abs(d.x),Mathf.Abs(d.y));return edge>=16f&&edge<=19f;});
            ink|=Segment(point,new Vector2(55,43),new Vector2(52,55))||Segment(point,new Vector2(54,72),new Vector2(58,78))||
                Segment(point,new Vector2(70,108),new Vector2(74,121))||Segment(point,new Vector2(68,121),new Vector2(81,118));
            if(ink)pixels[y*size+x]=new Color32(26,20,17,255);
        }
        tex.SetPixels32(pixels);tex.Apply();
        var path=Generated+"/Crystal Gourd icon.png";File.WriteAllBytes(path,tex.EncodeToPNG());Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static bool Segment(Vector2 p,Vector2 a,Vector2 b)
    {var d=b-a;return (p-(a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude))).sqrMagnitude<4f;}

}
