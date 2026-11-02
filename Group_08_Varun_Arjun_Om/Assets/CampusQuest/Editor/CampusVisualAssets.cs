#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
namespace CampusQuest.Editor {
public static class CampusVisualAssets {
 public const string Root="Assets/CampusQuest/Generated/VisualUpgrade";
 public static Material Wall,Tile,Wood,Steel,Dark,White,Blue,Green,Red,Gold,Screen,Poster,Ceiling;
 public static void Prepare(int resolution=512){
 Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Textures");Directory.CreateDirectory(Root+"/Prefabs");Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.Refresh();
 Wall=Make("V_Plaster",new Color(.85f,.84f,.79f),"plaster",resolution,0,.12f);
 Tile=Make("V_TerrazzoTiles",Color.white,"tile",resolution,0,.25f);
 Wood=Make("V_Wood",Color.white,"wood",resolution,0,.22f);
 Steel=Make("V_BrushedSteel",new Color(.72f,.76f,.78f),"steel",resolution,.65f,.38f);
 Dark=Make("V_Charcoal",new Color(.055f,.075f,.09f),null,resolution,0,.2f);
 White=Make("V_OffWhite",new Color(.94f,.94f,.89f),null,resolution,0,.15f);
 Blue=Make("V_Indigo",new Color(.075f,.18f,.29f),null,resolution,0,.15f);
 Green=Make("V_Sage",new Color(.22f,.38f,.29f),null,resolution,0,.15f);
 Red=Make("V_CampusMaroon",new Color(.44f,.055f,.09f),null,resolution,0,.15f);
 Gold=Make("V_Ochre",new Color(.71f,.47f,.16f),null,resolution,0,.15f);
 Screen=Make("V_ComputerScreen",Color.white,"screen",resolution,0,.3f);
 Poster=Make("V_PosterArtwork",Color.white,"poster",resolution,0,0);
 Ceiling=Make("V_Ceiling",new Color(.93f,.93f,.9f),null,resolution,0,0);
 }
 static Material Make(string name,Color tint,string pattern,int size,float metallic,float smooth){
 string path=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
 if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));AssetDatabase.CreateAsset(m,path);}
 m.name=name;m.color=tint;if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smooth);
 if(pattern!=null)m.mainTexture=Texture(pattern,size);
 EditorUtility.SetDirty(m);return m;
 }
 static Texture2D Texture(string type,int size){
 string path=Root+"/Textures/"+type+".png";var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing!=null)return existing;
 var t=new Texture2D(size,size,TextureFormat.RGB24,false);var pixels=new Color[size*size];var rng=new System.Random(729);
 for(int y=0;y<size;y++)for(int x=0;x<size;x++){
 float u=x/(float)size,v=y/(float)size,n=(float)rng.NextDouble();Color c;
 switch(type){
 case "tile":{bool seam=x%(size/2)<3||y%(size/2)<3;float f=.72f+(n-.5f)*.045f;bool fleck=n>.985f;c=seam?new Color(.46f,.48f,.47f):fleck?new Color(.54f,.56f,.53f):new Color(f+.055f,f+.045f,f);break;}
 case "wood":{float grain=Mathf.Sin(v*260+Mathf.Sin(u*25)*2)*.035f+(n-.5f)*.025f;c=new Color(.48f+grain,.29f+grain,.135f+grain*.6f);break;}
 case "steel":{float a=.78f+(Mathf.Sin(v*1800)*.025f)+(n-.5f)*.035f;c=new Color(a,a+.01f,a+.02f);break;}
 case "screen":{c=new Color(.025f,.09f,.15f);if(u>.07f&&u<.93f&&v>.1f&&v<.9f)c=new Color(.07f,.17f,.23f);if(v>.78f&&v<.84f&&u>.12f&&u<.75f)c=new Color(.16f,.6f,.7f);if(u>.12f&&u<.82f&&v>.2f&&v<.68f&&((int)(v*40))%3==0)c=new Color(.23f,.4f,.48f);break;}
 case "poster":{c=new Color(.91f,.82f,.61f);if(v>.62f)c=new Color(.11f,.24f,.36f);float d=Vector2.Distance(new Vector2(u,v),new Vector2(.5f,.6f));if(d<.19f)c=new Color(.72f,.22f,.13f);if(v<.3f&&u>.1f&&u<.9f&&((int)(v*30))%3==0)c=new Color(.2f,.25f,.26f);break;}
 default:{float f=.98f+(n-.5f)*.035f;c=new Color(f,f,f);break;}
 }
 pixels[y*size+x]=c;
 }
 t.SetPixels(pixels);t.Apply();File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);AssetDatabase.ImportAsset(path);
 var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.wrapMode=type=="screen"||type=="poster"?TextureWrapMode.Clamp:TextureWrapMode.Repeat;importer.mipmapEnabled=true;importer.maxTextureSize=size;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 }
 public static bool Tiled(Material m)=>m==Wall||m==Tile||m==Wood||m==Steel;
 public static GameObject Box(Transform parent,string name,Vector3 p,Vector3 size,Material material,bool collider=false){
 var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=size;go.GetComponent<MeshRenderer>().sharedMaterial=material;
 if(!collider)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
 }
 public static TextMesh Label(Transform parent,string text,Vector3 p,float size=.065f){
 var go=new GameObject("Mounted label");go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(0,180,0);
 var tm=go.AddComponent<TextMesh>();tm.text=text;tm.fontSize=64;tm.characterSize=size;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=Color.white;tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");go.GetComponent<MeshRenderer>().sharedMaterial=tm.font.material;return tm;
 }
 public static void BuildKit(){foreach(var kind in new[]{"ClassroomDesk","OfficeDesk","ComputerDesk","SecurityDesk","LabBench","Bench","Bookshelf","Washbasin","Counter"}){
 var root=new GameObject("DetailKit_"+kind+"_PLACEHOLDER");var p=root.transform;
 if(kind=="Bookshelf"){
 Box(p,"Cabinet back",new Vector3(0,1,-.02f),new Vector3(1.6f,2,.08f),Wood);
 foreach(float x in new[]{-.77f,.77f})Box(p,"Side",new Vector3(x,1,-.24f),new Vector3(.08f,2,.5f),Wood);
 for(int row=0;row<5;row++){float y=.1f+row*.39f;Box(p,"Shelf",new Vector3(0,y,-.23f),new Vector3(1.55f,.06f,.5f),Wood);
 if(row==4)continue;for(int b=0;b<9;b++){float h=.23f+(b%3)*.035f;var colors=new[]{Blue,Red,Green,Gold};Box(p,"Book spine",new Vector3(-.65f+b*.16f,y+h/2+.04f,-.28f),new Vector3(.12f,h,.27f),colors[(b+row)%4]);Box(p,"Spine band",new Vector3(-.65f+b*.16f,y+.13f,-.423f),new Vector3(.1f,.025f,.008f),White);}}
 Collider(p,new Vector3(0,1,-.22f),new Vector3(1.65f,2,.55f));
 }else if(kind=="Bench"){
 Box(p,"Seat",new Vector3(0,.48f,0),new Vector3(1.55f,.1f,.42f),Wood);foreach(float x in new[]{-.6f,.6f})Box(p,"Leg",new Vector3(x,.24f,0),new Vector3(.07f,.48f,.34f),Dark);Collider(p,new Vector3(0,.25f,0),new Vector3(1.55f,.5f,.42f));
 }else if(kind=="Washbasin"){
 Box(p,"Vanity",new Vector3(0,.43f,0),new Vector3(1.5f,.85f,.55f),White);Box(p,"Sink inset",new Vector3(0,.88f,-.04f),new Vector3(.7f,.04f,.37f),Steel);Box(p,"Tap",new Vector3(0,1.02f,.14f),new Vector3(.045f,.24f,.045f),Steel);Box(p,"Spout",new Vector3(0,1.12f,.07f),new Vector3(.045f,.045f,.18f),Steel);Collider(p,new Vector3(0,.45f,0),new Vector3(1.5f,.9f,.6f));
 }else{
 float h=kind=="Counter"?1.05f:.76f;Box(p,"Worktop",new Vector3(0,h,0),new Vector3(1.55f,.09f,.7f),Wood);
 foreach(float x in new[]{-.63f,.63f})foreach(float z in new[]{-.25f,.25f})Box(p,"Steel leg",new Vector3(x,h/2,z),new Vector3(.055f,h,.055f),Dark);
 if(kind=="ClassroomDesk"){Box(p,"Attached bench",new Vector3(0,.45f,-.68f),new Vector3(1.5f,.09f,.32f),Wood);foreach(float x in new[]{-.6f,.6f})Box(p,"Bench support",new Vector3(x,.23f,-.68f),new Vector3(.06f,.46f,.3f),Dark);}
 if(kind=="OfficeDesk"||kind=="ComputerDesk"||kind=="SecurityDesk"){Box(p,"Chair seat",new Vector3(0,.46f,-.67f),new Vector3(.48f,.1f,.4f),Blue);Box(p,"Chair back",new Vector3(0,.76f,-.85f),new Vector3(.48f,.58f,.06f),Blue);Box(p,"Chair support",new Vector3(0,.23f,-.67f),new Vector3(.12f,.46f,.12f),Dark);}
 if(kind=="ComputerDesk"||kind=="SecurityDesk"){
 int count=kind=="SecurityDesk"?3:1;for(int i=0;i<count;i++){float x=count==1?-.1f:(i-1)*.48f;float w=count==1?.65f:.43f;
 Box(p,"Monitor foot",new Vector3(x,.83f,.1f),new Vector3(.26f,.04f,.2f),Dark);Box(p,"Monitor stem",new Vector3(x,1,.15f),new Vector3(.06f,.3f,.06f),Dark);Box(p,"Monitor bezel",new Vector3(x,1.2f,.15f),new Vector3(w,.4f,.06f),Dark);Box(p,"Screen - sample UI",new Vector3(x,1.2f,.113f),new Vector3(w-.05f,.34f,.008f),Screen);}
 Box(p,"Keyboard",new Vector3(-.1f,.82f,-.18f),new Vector3(.5f,.035f,.18f),Dark);for(int i=0;i<7;i++)Box(p,"Key row",new Vector3(-.31f+i*.07f,.84f,-.18f),new Vector3(.04f,.012f,.12f),Steel);Box(p,"Mouse",new Vector3(.34f,.84f,-.16f),new Vector3(.075f,.04f,.12f),Dark);Box(p,"Computer tower",new Vector3(.57f,.31f,.05f),new Vector3(.22f,.58f,.4f),Dark);
 }
 Collider(p,new Vector3(0,.4f,kind=="Counter"||kind=="LabBench"?0:-.22f),new Vector3(1.6f,.85f,kind=="Counter"||kind=="LabBench"?.75f:1.3f));
 }
 PrefabUtility.SaveAsPrefabAsset(root,Root+"/Prefabs/"+kind+".prefab");UnityEngine.Object.DestroyImmediate(root);
 }}
 static void Collider(Transform p,Vector3 center,Vector3 size){var c=p.gameObject.AddComponent<BoxCollider>();c.center=center;c.size=size;}
 public static void Kit(string kind,Transform parent,Vector3 position,Quaternion rotation){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+kind+".prefab");var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);PrefabUtility.UnpackPrefabInstance(go,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=rotation;go.name="Furniture_"+kind+"_PLACEHOLDER";}
}
}
#endif
