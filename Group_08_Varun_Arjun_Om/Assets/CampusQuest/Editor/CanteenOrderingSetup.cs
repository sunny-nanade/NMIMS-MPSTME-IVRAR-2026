#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace CampusQuest.Editor {
public static class CanteenOrderingSetup {
 const string Root="Assets/CampusQuest/Generated";
 const string RigName="Canteen ordering interactions";
 [Serializable] class MenuJSON { public MenuEntry[] items; }
 [Serializable] class MenuEntry { public string id,name; public int price; public bool isPlaceholder; }
 static T Find<T>(Scene scene) where T:Component {return scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<T>(true)).FirstOrDefault();}
 static Material Material(string name,Color color){
 Directory.CreateDirectory(Root+"/Materials");AssetDatabase.Refresh();
 string path=Root+"/Materials/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
 mat=new Material(Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard"));mat.color=color;AssetDatabase.CreateAsset(mat,path);return mat;
 }
 static MenuItemData[] Menu(){
 Directory.CreateDirectory(Root+"/Menu");AssetDatabase.Refresh();
 var seed=JsonUtility.FromJson<MenuJSON>(File.ReadAllText("Assets/CampusQuest/Layouts/menu.json"));
 return seed.items.Select(entry=>{
  string path=Root+"/Menu/"+entry.id+".asset";var item=AssetDatabase.LoadAssetAtPath<MenuItemData>(path);
  if(item==null){item=ScriptableObject.CreateInstance<MenuItemData>();item.itemID=entry.id;item.displayName=entry.name;item.priceINR=entry.price;item.isPlaceholder=entry.isPlaceholder;item.isVerified=!entry.isPlaceholder;AssetDatabase.CreateAsset(item,path);}
  return item;
 }).ToArray();
 }
 public static void WireCore(Scene scene){
 var manager=Find<CanteenOrderManager>(scene);
 if(manager==null){var go=new GameObject("Canteen order session");SceneManager.MoveGameObjectToScene(go,scene);manager=go.AddComponent<CanteenOrderManager>();}
 if(manager.menuItems==null||manager.menuItems.Length==0)manager.menuItems=Menu();
 manager.propMaterial=Material("Coupon paper",new Color(.96f,.91f,.72f));EditorUtility.SetDirty(manager);
 }
 static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material mat){
 var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;return go;
 }
 static TextMesh Text(Transform parent,string name,string text,Vector3 position,float yaw,float size){
 var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=Quaternion.Euler(0,yaw,0);
 var tm=go.AddComponent<TextMesh>();tm.text=text;tm.characterSize=size;tm.fontSize=48;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=Color.black;
 tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");go.GetComponent<MeshRenderer>().sharedMaterial=tm.font.material;return tm;
 }
 public static void WireCanteen(Scene scene){
 if(!scene.IsValid()||!scene.isLoaded)throw new InvalidOperationException("Cannot wire canteen: scene is invalid or unloaded.");
 var matches=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EnvironmentRoot>(true)).Where(e=>e.config!=null&&e.config.level==-1).ToArray();
 if(matches.Length!=1)throw new InvalidOperationException("Cannot wire canteen in scene '"+scene.name+"': expected one Level -1 EnvironmentRoot, found "+matches.Length+".");
 WireCanteenEnvironment(matches[0]);
 }
 // Builders already own the exact environment; no scene-wide rediscovery is needed.
 public static void WireCanteenEnvironment(EnvironmentRoot env){
 if(env==null)throw new InvalidOperationException("Canteen builder supplied a null EnvironmentRoot.");
 if(env.config==null)throw new InvalidOperationException("Canteen EnvironmentRoot has no FloorConfig: "+env.name);
 if(env.config.level!=-1)throw new InvalidOperationException("Canteen builder received Level "+env.config.level+" from config "+env.config.name+"; expected -1.");
 // Replace only the owned interaction rig; architecture and unrelated objects remain intact.
 foreach(var t in env.GetComponentsInChildren<Transform>(true).Where(t=>t.name==RigName).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
 var root=new GameObject(RigName);root.transform.SetParent(env.transform,false);
 var accent=Material("Ordering interaction red",new Color(.75f,.08f,.09f));var paper=Material("Coupon paper",new Color(.96f,.91f,.72f));var steel=Material("Food tray",new Color(.5f,.65f,.64f));
 var board=Box(root.transform,"Menu board — E to choose",new Vector3(20.2f,1.45f,15.15f),new Vector3(.8f,.55f,.1f),accent);
 var menu=board.AddComponent<MenuBoard>();var menuUse=board.AddComponent<InteractableObject>();menuUse.menuBoard=menu;menuUse.prompt="Open canteen menu";
 Text(root.transform,"Menu instructions","MENU — E",new Vector3(20.2f,1.45f,15.22f),180,.055f);
 var couponRoot=new GameObject("Coupon pickup");couponRoot.transform.SetParent(root.transform,false);var coupon=couponRoot.AddComponent<Coupon>();
 var cv=new GameObject("Coupon visual");cv.transform.SetParent(couponRoot.transform,false);coupon.visual=cv;
 var cb=Box(cv.transform,"Coupon",new Vector3(22.1f,1.4f,15.15f),new Vector3(1.05f,.55f,.08f),paper);var ci=cb.AddComponent<InteractableObject>();ci.coupon=coupon;ci.prompt="Collect coupon";
 coupon.label=Text(cv.transform,"Coupon print","COUPON",new Vector3(22.1f,1.4f,15.21f),180,.035f);cv.SetActive(false);
 var hand=Box(root.transform,"Coupon handover point",new Vector3(12.85f,1.45f,22.5f),new Vector3(.12f,.65f,1f),accent);
 var serve=hand.AddComponent<ServingCounter>();var si=hand.AddComponent<InteractableObject>();si.servingCounter=serve;si.prompt="Hand coupon to serving counter";
 Text(root.transform,"Handover instructions","COUPON HERE\nE — hand over",new Vector3(12.93f,1.45f,22.5f),270,.05f);
 var trayRoot=new GameObject("Tray pickup");trayRoot.transform.SetParent(root.transform,false);var tray=trayRoot.AddComponent<FoodTray>();
 var tv=new GameObject("Tray visual");tv.transform.SetParent(trayRoot.transform,false);tray.visual=tv;
 var tb=Box(tv.transform,"Food tray",new Vector3(12.2f,1.12f,25),new Vector3(.75f,.12f,1.05f),steel);var ti=tb.AddComponent<InteractableObject>();ti.foodTray=tray;ti.prompt="Collect food tray";
 var food=Box(tv.transform,"Food — placeholder geometry",new Vector3(12.2f,1.25f,25),new Vector3(.4f,.15f,.5f),paper);UnityEngine.Object.DestroyImmediate(food.GetComponent<Collider>());
 tray.label=Text(tv.transform,"Tray instructions","TRAY",new Vector3(12.85f,1.65f,25),270,.05f);tv.SetActive(false);
 foreach(var text in env.GetComponentsInChildren<TextMesh>(true))if(text.gameObject.name.StartsWith("Sign_ORDER COUNTER"))text.text="ORDER COUNTER";
 }
 [MenuItem("Campus Quest/8. Install canteen ordering")]
 public static void Install(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Exit Play mode first.");return;}
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 string core=Root+"/Scenes/Core.unity",canteen=Root+"/Scenes/Scene_09_Canteen.unity";
 if(!File.Exists(core)||!File.Exists(canteen)){Debug.LogError("Generate the campus and apply canteen drawing update first.");return;}
 CanteenOrderingTests.Run();
 var scene=EditorSceneManager.OpenScene(core,OpenSceneMode.Single);WireCore(scene);EditorSceneManager.SaveScene(scene);
 scene=EditorSceneManager.OpenScene(canteen,OpenSceneMode.Single);WireCanteen(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
 Validate();EditorSceneManager.OpenScene(core,OpenSceneMode.Single);
 Debug.Log("Canteen ordering installed. Core is open. Play, take lift to C, press E on red MENU board.");
 }
 [MenuItem("Campus Quest/9. Validate canteen ordering")]
 public static void Validate(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Exit Play mode first.");return;}
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 CanteenOrderingTests.Run();
 var core=EditorSceneManager.OpenScene(Root+"/Scenes/Core.unity",OpenSceneMode.Single);var manager=Find<CanteenOrderManager>(core);
 if(manager==null||manager.menuItems==null||manager.menuItems.Length!=4||manager.menuItems.Any(i=>i==null))throw new InvalidOperationException("Order manager/menu not wired.");
 var scene=EditorSceneManager.OpenScene(Root+"/Scenes/Scene_09_Canteen.unity",OpenSceneMode.Single);
 var coupon=Find<Coupon>(scene);var tray=Find<FoodTray>(scene);
 if(Find<MenuBoard>(scene)==null||Find<ServingCounter>(scene)==null||coupon==null||coupon.visual==null||coupon.label==null||tray==null||tray.visual==null||tray.label==null)throw new InvalidOperationException("Ordering interaction references missing.");
 var interactions=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<InteractableObject>(true)).ToArray();
 if(interactions.Count(i=>i.menuBoard!=null)!=1||interactions.Count(i=>i.coupon!=null)!=1||interactions.Count(i=>i.servingCounter!=null)!=1||interactions.Count(i=>i.foodTray!=null)!=1)throw new InvalidOperationException("Ordering targets missing or duplicated.");
 EditorSceneManager.OpenScene(Root+"/Scenes/Core.unity",OpenSceneMode.Single);Debug.Log("Canteen ordering validation: Errors: 0. Runtime input and visuals still need Play testing.");
 }
}
}
#endif
