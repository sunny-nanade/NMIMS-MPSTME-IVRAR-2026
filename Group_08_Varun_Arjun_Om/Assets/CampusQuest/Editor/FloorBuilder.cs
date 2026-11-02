#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace CampusQuest.Editor {
public static class FloorBuilder {
 const string Root="Assets/CampusQuest", Generated=Root+"/Generated";
 static Material wall,floor,wood,accent,metal,glass;
 static Transform parent;
 [Serializable] class ConfigJSON {public int level;public RoomData[] rooms;}
 class Edge {public bool horizontal;public float fixedAt,start,end;public List<Vector2> openings=new List<Vector2>();}
 [MenuItem("Campus Quest/1. Generate or rebuild campus")]
 [MenuItem("Campus Quest/0. Apply typical floors and rear washrooms")]
 public static void Generate(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Exit Play mode first.");return;}
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 Directory.CreateDirectory(Generated+"/Scenes");Directory.CreateDirectory(Generated+"/Configs");Directory.CreateDirectory(Generated+"/Materials");Directory.CreateDirectory(Generated+"/Prefabs");AssetDatabase.Refresh();
 wall=Mat("Warm plaster",new Color(.87f,.87f,.82f));floor=Mat("Stone floor",new Color(.48f,.53f,.56f));wood=Mat("Furniture",new Color(.49f,.28f,.13f));accent=Mat("Campus red",new Color(.62f,.04f,.08f));metal=Mat("Lift steel",new Color(.29f,.36f,.40f));glass=Mat("Atrium rail",new Color(.17f,.30f,.34f));
 BuildKit();var scenes=new List<EditorBuildSettingsScene>();
 for(int level=-1;level<=8;level++){
 string cfgPath=Generated+"/Configs/Floor_"+level+".asset";
 var config=AssetDatabase.LoadAssetAtPath<FloorConfig>(cfgPath);
 // Preserve user changes in existing FloorConfigs; reset explicitly via a separate menu.
 if(config==null){config=ScriptableObject.CreateInstance<FloorConfig>();config.level=level;config.sceneName=FloorConfig.SceneFor(level);config.displayName=FloorConfig.Label(level);config.layoutJSON=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Layouts/"+(level==-1?"canteen":level==0?"ground":"typical")+".json");config.rooms=JsonUtility.FromJson<ConfigJSON>(File.ReadAllText(Root+"/Layouts/floor_"+level+".json")).rooms;if(level==7)config.excludedRoomIDs=new[]{"right_0","right_1"};AssetDatabase.CreateAsset(config,cfgPath);}
 MigrateLayout(config);
 var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
 Build(config);
 var path=Generated+"/Scenes/"+config.sceneName+".unity";EditorSceneManager.SaveScene(scene,path);scenes.Add(new EditorBuildSettingsScene(path,true));
 }
 var core=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);BuildCore();string corePath=Generated+"/Scenes/Core.unity";EditorSceneManager.SaveScene(core,corePath);
 scenes.Insert(0,new EditorBuildSettingsScene(corePath,true));
 // Retain unrelated scene entries while avoiding duplicates for generated scenes.
 var generatedPaths=new HashSet<string>(scenes.Select(s=>s.path));scenes.AddRange(EditorBuildSettings.scenes.Where(s=>!generatedPaths.Contains(s.path)));
 EditorBuildSettings.scenes=scenes.ToArray();AssetDatabase.SaveAssets();AssetDatabase.Refresh();
 if(CampusVisualUpgrade.Installed)CampusVisualUpgrade.RefreshAtriumContext();
 Debug.Log("Campus generated: Core + ten environments. Open Core and press Play. Layout remains APPROXIMATE; run Campus Quest/Validate generated scenes.");
 }
 // Idempotent migration; retain assets and unaffected room-label edits.
 static void MigrateLayout(FloorConfig config){
 // Retire the rear room block on every rebuild, including existing FloorConfig assets.
 if(config.level>0){
 config.excludedRoomIDs=(config.excludedRoomIDs??new string[0]).Concat(new[]{"inner_south_L","inner_south_R"}).Distinct().ToArray();
 config.rooms=(config.rooms??new RoomData[0]).Where(r=>r.id!="inner_south_L"&&r.id!="inner_south_R").ToArray();
 EditorUtility.SetDirty(config);
 }

 if(config.level==-1){
  if(config.layoutRevision>=6)return;
  config.layoutJSON=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Layouts/canteen.json");
  config.rooms=JsonUtility.FromJson<ConfigJSON>(File.ReadAllText(Root+"/Layouts/floor_-1.json")).rooms;
  config.excludedRoomIDs=new string[0];config.layoutRevision=6;EditorUtility.SetDirty(config);return;
 }
 if(config.layoutRevision>=5)return;
 var seed=JsonUtility.FromJson<ConfigJSON>(File.ReadAllText(Root+"/Layouts/floor_"+config.level+".json"));
 var oldRooms=config.rooms??new RoomData[0];
 if(config.level==0){
  var changed=new HashSet<string>{"inner_north_L","inner_north_R","washroom_front_right","security","rear_left_room","front_0","washroom_rear"};
  var removed=new HashSet<string>{"washrooms_back","reception","inner_south_L","inner_south_R","front_1","front_2","front_3"};
  var kept=oldRooms.Where(r=>!removed.Contains(r.id)&&!changed.Contains(r.id)).ToList();
  kept.AddRange(seed.rooms.Where(r=>changed.Contains(r.id)));config.rooms=kept.ToArray();
 }else if(config.level==-1){
  var kept=oldRooms.Where(r=>r.id!="front_2"&&r.id!="washroom_rear").ToList();
  kept.AddRange(seed.rooms.Where(r=>r.id=="washroom_rear"));config.rooms=kept.ToArray();
 }else{
  var retained=new HashSet<string>{"left_0","left_1","left_2","right_0","right_1","inner_south_L","inner_south_R","inner_north_L","inner_north_R"};
  foreach(var room in seed.rooms){
   // Preserve actual manual names only; do not carry stale generated numbering/lab placeholders forward.
   var old=oldRooms.FirstOrDefault(r=>r.id==room.id);
   if(old!=null&&retained.Contains(room.id)&&!old.isPlaceholder){room.label=old.label;room.category=old.category;room.description=old.description;room.isVerified=old.isVerified;room.isPlaceholder=old.isPlaceholder;}
  }
  // Unrecognized custom additions are kept; obsolete known kit IDs are intentionally retired.
  var retired=new HashSet<string>{"front_0","front_1","front_2","front_3","right_2","washrooms_back","library"};
  var merged=seed.rooms.ToList();
  merged.AddRange(oldRooms.Where(r=>!retired.Contains(r.id)&&!seed.rooms.Any(n=>n.id==r.id)&&!(config.level==7&&(r.id=="right_0"||r.id=="right_1"))));
  config.rooms=merged.ToArray();
  if(config.level==7){
   var libraryIDs=new HashSet<string>{"left_0","left_1","left_2","right_0","right_1"};
   config.excludedRoomIDs=(config.excludedRoomIDs??new string[0]).Where(id=>!libraryIDs.Contains(id)).Concat(new[]{"right_0","right_1"}).Distinct().ToArray();
  }
 }
 config.layoutRevision=5;EditorUtility.SetDirty(config);
 }
 [MenuItem("Campus Quest/Reset generated FloorConfigs from supplied JSON")]
 static void ResetConfigs(){if(!EditorUtility.DisplayDialog("Reset configs?","This discards edits to generated FloorConfigs. Source layouts and scripts are retained.","Reset","Cancel"))return;foreach(string guid in AssetDatabase.FindAssets("t:FloorConfig",new[]{Generated+"/Configs"}))AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));}
 static Material Mat(string name,Color color){string path=Generated+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(m!=null)return m;var shader=Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");m=new Material(shader){name=name,color=color};AssetDatabase.CreateAsset(m,path);return m;}
 static GameObject Box(string name,Vector3 p,Vector3 size,Material material,bool collider=true){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;if(!collider)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;}
 static TextMesh Sign(string text,Vector3 p,float yaw=0,float size=.15f){var go=new GameObject("Sign_"+text.Replace("\n","_"));go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(0,yaw,0);var tm=go.AddComponent<TextMesh>();tm.text=text;tm.fontSize=48;tm.characterSize=size;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.color=new Color(.7f,.04f,.07f);var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");tm.font=font;go.GetComponent<MeshRenderer>().sharedMaterial=font.material;return tm;}
 static void Spawn(string id,Vector3 p,float yaw){var go=new GameObject("Spawn_"+id);go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(0,yaw,0);go.AddComponent<SpawnPoint>().id=id;}
 static void Kit(string kind,Vector3 p,float yaw=0){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Generated+"/Prefabs/"+kind+".prefab");var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab);go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localRotation=Quaternion.Euler(0,yaw,0);}
 static void BuildKit(){
 foreach(string kind in new[]{"Desk","Bench","Bookshelf","LabBench","Counter","Washbasin"}){
 string path=Generated+"/Prefabs/"+kind+".prefab";if(AssetDatabase.LoadAssetAtPath<GameObject>(path)!=null)continue;
 var root=new GameObject(kind+"_APPROXIMATE");parent=root.transform;
 if(kind=="Bookshelf"){Box("back",new Vector3(0,1,0),new Vector3(1.5f,2,.12f),wood);for(int i=0;i<5;i++)Box("shelf",new Vector3(0,.1f+i*.45f,-.2f),new Vector3(1.5f,.07f,.5f),wood);}
 else {float h=kind=="Bench"?.45f:kind=="Counter"?1.05f:.75f;Box("top",new Vector3(0,h,0),new Vector3(1.5f,.12f,.65f),kind=="Washbasin"?wall:wood);foreach(float x in new[]{-.6f,.6f})foreach(float z in new[]{-.23f,.23f})Box("leg",new Vector3(x,h/2,z),new Vector3(.08f,h,.08f),metal);}
 PrefabUtility.SaveAsPrefabAsset(root,path);UnityEngine.Object.DestroyImmediate(root);
 }
 }
 static void Build(FloorConfig config){
 var data=config.ReadLayout();var root=new GameObject("Floor_"+config.displayName+"__APPROXIMATE");root.AddComponent<EnvironmentRoot>().config=config;parent=root.transform;
 var holes=new List<Rect>();if(data.hasAtrium)holes.Add(data.atrium.Bounds);holes.AddRange(data.stairs.Select(s=>new Rect(s.x,s.z,8,s.depth)));
 Slab(data.width,data.depth,holes,0,"Floor");
 var ceilingHoles=new List<Rect>(holes);ceilingHoles.AddRange((data.ceilingOpenings??new RectData[0]).Select(h=>h.Bounds));
 Slab(data.width,data.depth,ceilingHoles,3.2f,"Ceiling");
 var edges=new List<Edge>();
 // Boundary opening is a staging entrance, not an outside campus model.
 AddRect(edges,new Rect(0,0,data.width,data.depth),config.level==0?"N":"",.5f,2.4f);
 foreach(var room in config.ResolvedRooms()){AddRect(edges,room.Bounds,room.doorSide,room.doorOffset,room.doorWidth);foreach(float extra in (room.extraDoorOffsets??new float[0]))AddRect(edges,room.Bounds,room.doorSide,extra,room.doorWidth);foreach(var door in (room.additionalDoors??new AdditionalDoorData[0]))AddRect(edges,room.Bounds,door.side,door.offset,door.width);}
 foreach(var s in data.stairs){
 var side=string.IsNullOrEmpty(s.entrySide)?(s.entryEast?"E":"W"):s.entrySide;
 AddRect(edges,new Rect(s.x,s.z,8,s.depth),side,(side=="N"||side=="S")?.5f:1f/s.depth,1.65f);
 }
 BuildWalls(edges,data.wallHeight,data.wallThickness);
 if(data.hasAtrium){var a=data.atrium.Bounds;Box("Atrium guard",new Vector3(a.center.x,.6f,a.yMin),new Vector3(a.width,1.2f,.12f),glass);Box("Atrium guard",new Vector3(a.center.x,.6f,a.yMax),new Vector3(a.width,1.2f,.12f),glass);Box("Atrium guard",new Vector3(a.xMin,.6f,a.center.y),new Vector3(.12f,1.2f,a.height),glass);Box("Atrium guard",new Vector3(a.xMax,.6f,a.center.y),new Vector3(.12f,1.2f,a.height),glass);}
 foreach(var r in config.ResolvedRooms())Dress(r);
 foreach(var stair in data.stairs)Stair(stair,config.level);
 foreach(var lift in data.lifts)Lift(lift,config.level);
 if(config.level==0){
 Spawn("start",new Vector3(20,.1f,54),180);
 Box("Entry porch",new Vector3(20,-.1f,57),new Vector3(4,.2f,2),floor);
 Sign("MAIN ENTRANCE",new Vector3(20,2.6f,55.8f),0,.17f);
 Sign("MAIN ENTRANCE",new Vector3(20,2.6f,56.2f),180,.17f);
 }
 else Spawn("start",new Vector3(28,.1f,8.5f),270);
 DirectoryBoard(config);
 if(config.level==-1)
 {
     DressCanteen();
     var env = root.GetComponent<EnvironmentRoot>();
     env.config = AssetDatabase.LoadAssetAtPath<FloorConfig>(
         "Assets/CampusQuest/Generated/Configs/Floor_-1.asset");
     if (env.config == null)
         throw new InvalidOperationException("Cannot reload canteen FloorConfig at Assets/CampusQuest/Generated/Configs/Floor_-1.asset.");
     EditorUtility.SetDirty(env);
     CanteenOrderingSetup.WireCanteenEnvironment(env);
 }
 if(MissionSetup.Installed)MissionSetup.WireEnvironment(root.scene);
 if(CampusVisualUpgrade.Installed)CampusVisualUpgrade.Decorate(root.GetComponent<EnvironmentRoot>());
 RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.8f,.8f,.8f);
 // Shared materials and static meshes; lights/bakes are a later optimization pass.
 foreach(var renderer in root.GetComponentsInChildren<MeshRenderer>()){if(renderer.GetComponentInParent<LiftDoor>()==null&&renderer.GetComponentInParent<HingedCampusDoor>()==null&&renderer.GetComponent<TextMesh>()==null)GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccludeeStatic|StaticEditorFlags.OccluderStatic);}
 }
 static void DressCanteen(){
 // Drawing has front at +Z, and its lower entry corridor at +X.
 Box("Serving counter long APPROXIMATE",new Vector3(12.2f,.52f,23),new Vector3(.9f,1.04f,16),wood);
 Box("Serving counter return APPROXIMATE",new Vector3(14.3f,.52f,14.7f),new Vector3(5.1f,1.04f,.9f),wood);
 Box("Order counter APPROXIMATE",new Vector3(21,.52f,14.7f),new Vector3(3,1.04f,.9f),wood);
 Box("Kiosk APPROXIMATE",new Vector3(26,1,14.7f),new Vector3(2,2,1),wood);
 Sign("FOOD SERVING",new Vector3(12.8f,2,23),270,.13f);
 Sign("ORDER COUNTER\nVada pav 20 | Dosa 50\nMaggi 40 | Water 20",new Vector3(21,2,15.3f),180,.10f);
 Sign("KIOSK",new Vector3(26,2.4f,15.3f),180,.12f);
 // Furniture is approximate. Keep a 2m aisle through both student entrances.
 foreach(float z in new[]{19f,27f})foreach(float x in new[]{17f,23f}){
 Kit("Desk",new Vector3(x,0,z));Kit("Bench",new Vector3(x,0,z-1));Kit("Bench",new Vector3(x,0,z+1));}
 foreach(float x in new[]{21f,25f})foreach(float z in new[]{36.5f,40f}){Kit("Desk",new Vector3(x,0,z));Kit("Bench",new Vector3(x,0,z+.9f));}
 Kit("Desk",new Vector3(14,0,38));Kit("Bench",new Vector3(14,0,39));
 Sign("ENTRY 3",new Vector3(29.2f,2.5f,16),270,.12f);
 Sign("ENTRY 2",new Vector3(29.2f,2.5f,24),270,.12f);
 Sign("ENTRY 1 — AC CANTEEN",new Vector3(29.2f,2.5f,37),270,.1f);
 }
 [MenuItem("Campus Quest/15. Fix clear atrium and enclosed canteens")]
 public static void FixAtriumAndCanteens(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Stop Play mode first.");return;}
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 if(!CampusVisualUpgrade.Installed)throw new InvalidOperationException("Install the visual upgrade first.");
 // Preflight before writing any scenes. Preserve Core rig, mission and ordering assets.
 if(!File.Exists(Generated+"/Scenes/Core.unity"))throw new InvalidOperationException("Core scene missing.");
 for(int level=-1;level<=8;level++)if(AssetDatabase.LoadAssetAtPath<FloorConfig>(Generated+"/Configs/Floor_"+level+".asset")==null)throw new InvalidOperationException("Missing FloorConfig "+level);
 wall=Mat("Warm plaster",new Color(.87f,.87f,.82f));floor=Mat("Stone floor",new Color(.48f,.53f,.56f));wood=Mat("Furniture",new Color(.49f,.28f,.13f));accent=Mat("Campus red",new Color(.62f,.04f,.08f));metal=Mat("Lift steel",new Color(.29f,.36f,.40f));glass=Mat("Atrium rail",new Color(.17f,.30f,.34f));
 BuildKit();
 try{
 for(int level=-1;level<=8;level++){
 EditorUtility.DisplayProgressBar("Atrium correction","Rebuilding environment "+FloorConfig.Label(level),(level+1)/10f);
 var cfg=AssetDatabase.LoadAssetAtPath<FloorConfig>(Generated+"/Configs/Floor_"+level+".asset");MigrateLayout(cfg);
 var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
 Build(cfg);EditorSceneManager.SaveScene(scene,Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity");
 }
 AssetDatabase.SaveAssets();CampusVisualUpgrade.RefreshAtriumContext();CampusVisualUpgrade.Validate();
 Debug.Log("Atrium correction applied: aligned hollow on G–8; AC/faculty canteens roofed outside the opening. Test from Core in Play mode.");
 }finally{EditorUtility.ClearProgressBar();}
 }
 [MenuItem("Campus Quest/6. Apply canteen drawing update")]
 static void ApplyCanteen(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Exit Play mode first.");return;}
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 // Existing project required; preserve Core and floors 1–8.
 wall=Mat("Warm plaster",new Color(.87f,.87f,.82f));floor=Mat("Stone floor",new Color(.48f,.53f,.56f));wood=Mat("Furniture",new Color(.49f,.28f,.13f));accent=Mat("Campus red",new Color(.62f,.04f,.08f));metal=Mat("Lift steel",new Color(.29f,.36f,.40f));glass=Mat("Atrium rail",new Color(.17f,.30f,.34f));
 foreach(int level in new[]{-1,0}){
 var config=AssetDatabase.LoadAssetAtPath<FloorConfig>(Generated+"/Configs/Floor_"+level+".asset");
 if(config==null){Debug.LogError("Generate campus first; missing FloorConfig "+level);return;}
 MigrateLayout(config);var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);Build(config);
 EditorSceneManager.SaveScene(scene,Generated+"/Scenes/"+config.sceneName+".unity");}
 AssetDatabase.SaveAssets();if(CampusVisualUpgrade.Installed)CampusVisualUpgrade.RefreshAtriumContext();ReviewLevel(-1);Debug.Log("Canteen drawing applied. Ground rear stair descent closed; front stairs and all lifts serve C. Test in Core.");
 }
 [MenuItem("Campus Quest/7. Review canteen from above")]
 static void ReviewCanteen(){ReviewLevel(-1);}
 static void Slab(float width,float depth,List<Rect> holes,float y,string name){
 var xs=new SortedSet<float>{0,width};var zs=new SortedSet<float>{0,depth};foreach(var h in holes){xs.Add(h.xMin);xs.Add(h.xMax);zs.Add(h.yMin);zs.Add(h.yMax);}var xx=xs.ToArray();var zz=zs.ToArray();
 for(int i=0;i<xx.Length-1;i++)for(int j=0;j<zz.Length-1;j++){var c=new Vector2((xx[i]+xx[i+1])/2,(zz[j]+zz[j+1])/2);if(holes.Any(h=>h.Contains(c)))continue;Box(name,new Vector3(c.x,y-.1f,c.y),new Vector3(xx[i+1]-xx[i],.2f,zz[j+1]-zz[j]),floor);}
 }
 static void AddRect(List<Edge> list,Rect r,string side,float offset,float door){
 foreach(string s in new[]{"S","N","W","E"}){bool h=s=="S"||s=="N";var e=new Edge{horizontal=h,fixedAt=h?(s=="S"?r.yMin:r.yMax):(s=="W"?r.xMin:r.xMax),start=h?r.xMin:r.yMin,end=h?r.xMax:r.yMax};if(s==side){float c=Mathf.Lerp(e.start,e.end,offset);e.openings.Add(new Vector2(c-door/2,c+door/2));}list.Add(e);}
 }
 static void BuildWalls(List<Edge> edges,float height,float thickness){
 foreach(var group in edges.GroupBy(e=>(e.horizontal?"H":"V")+e.fixedAt.ToString("F3",System.Globalization.CultureInfo.InvariantCulture))){var lines=group.ToList();var first=lines[0];var cuts=new SortedSet<float>();foreach(var e in lines){cuts.Add(e.start);cuts.Add(e.end);foreach(var o in e.openings){cuts.Add(o.x);cuts.Add(o.y);}}var a=cuts.ToArray();for(int i=0;i<a.Length-1;i++){float mid=(a[i]+a[i+1])/2;if(!lines.Any(e=>mid>=e.start&&mid<=e.end))continue;bool door=lines.Any(e=>e.openings.Any(o=>mid>o.x&&mid<o.y));float bottom=door?2.15f:0;var p=first.horizontal?new Vector3(mid,(bottom+height)/2,first.fixedAt):new Vector3(first.fixedAt,(bottom+height)/2,mid);var size=first.horizontal?new Vector3(a[i+1]-a[i],height-bottom,thickness):new Vector3(thickness,height-bottom,a[i+1]-a[i]);Box(door?"Door lintel":"Wall",p,size,wall);}}
 }
 static void Dress(RoomData r){
 var container=new GameObject((r.isPlaceholder?"PLACEHOLDER_":"SOURCE_ID_")+r.id);container.transform.SetParent(parent,false);var old=parent;parent=container.transform;
 var b=r.Bounds;if(r.category=="Restricted"){Sign("RESTRICTED AREA",new Vector3(b.center.x,2.2f,b.yMax+.15f),180,.12f);parent=old;return;}Vector3 label;float yaw;
 switch(r.doorSide){case "N":label=new Vector3(Mathf.Lerp(b.xMin,b.xMax,r.doorOffset),2.45f,b.yMax+.13f);yaw=180;break;case "W":label=new Vector3(b.xMin-.13f,2.45f,b.center.y);yaw=90;break;case "E":label=new Vector3(b.xMax+.13f,2.45f,b.center.y);yaw=270;break;default:label=new Vector3(Mathf.Lerp(b.xMin,b.xMax,r.doorOffset),2.45f,b.yMin-.13f);yaw=0;break;}
 var text=r.label+(r.isPlaceholder?"\n[PLACEHOLDER]":"")+"\n[Approximate geometry]";Sign(text,label,yaw,.085f);
 var button=Box("Information button",label+Vector3.down*.9f+((r.doorSide=="N"||r.doorSide=="S")?Vector3.right:Vector3.forward)*1.05f,new Vector3(.28f,.28f,.28f),accent);var interact=button.AddComponent<InteractableObject>();interact.prompt=r.label;interact.information=r.description+"\n\n"+(r.isPlaceholder?"PLACEHOLDER: do not use for assessed navigation or final quiz.":"Facility identity is source-supported; position and geometry are approximate.");
 if(r.category=="Canteen"){parent=old;return;}
 string kit=r.category=="Library"?"Bookshelf":r.category=="Lab"?"LabBench":r.category=="Washroom"?"Washbasin":r.category=="Canteen"?"Bench":"Desk";
 // Preserve a central 2 m clear route to every door.
 for(float z=b.yMin+2;z<b.yMax-1.5f;z+=2.6f){if(Mathf.Abs(z-b.center.y)<1.3f)continue;foreach(float x in new[]{b.xMin+2,b.xMax-2}){if(Mathf.Abs(x-b.center.x)<1.2f)continue;Kit(kit,new Vector3(x,0,z));}}
 parent=old;
 }
 static void Stair(StairData s,int level){
 var old=parent;var go=new GameObject("Stairwell_"+s.id+"_APPROXIMATE");go.transform.SetParent(parent,false);go.transform.localPosition=s.reverse?new Vector3(s.x+8,0,s.z+s.depth):new Vector3(s.x,0,s.z);
 go.transform.localRotation=Quaternion.Euler(0,s.reverse?180:0,0);parent=go.transform;
 Box("Entry landing",new Vector3(4,-.1f,1),new Vector3(8,.2f,2),floor);
 Spawn("stair:"+s.id,new Vector3(s.reverse?4:(s.entryEast?6.7f:1.3f),.1f,1),s.reverse?180:(s.entryEast?90:270));
 if(level<8)Flights(s.id,level+1,.4f,1);
 else {Box("Upper route closed",new Vector3(2,1,2),new Vector3(3.4f,2,.15f),wall);Sign("TOP FLOOR",new Vector3(2,1.6f,1.85f),0,.13f);}
 if(level> -1 && !(level==0 && s.id=="Rear"))Flights(s.id,level-1,4.2f,-1);
 else {Box("Lower route closed",new Vector3(5.9f,1,2),new Vector3(3.6f,2,.15f),wall);Sign(level==0?"NO REAR STAIRS TO CANTEEN\nUse front stairs or lifts":"LOWEST FLOOR",new Vector3(5.9f,1.6f,1.85f),0,.12f);}
 Sign("STAIRS "+s.id+"\nUP left | DOWN right",new Vector3(4,2.5f,1.8f),0,.10f);parent=old;
 }
 static void Flights(string id,int destination,float x,int direction){
 const float lane=1.4f,run=3.6f,rise=1.8f;
 Flight(x,2,0,run,rise*direction,lane);
 Box("Mid landing",new Vector3(x+1.5f,rise*direction-.1f,6.3f),new Vector3(3,.2f,1.4f),floor);
 Flight(x+1.6f,5.6f,rise*direction,-run,rise*direction,lane);
 float end=3.6f*direction;
 Box("Transition landing",new Vector3(x+2.3f,end-.1f,1.25f),new Vector3(lane,.2f,1.5f),floor);
 var trigger=new GameObject("Transition_"+destination);trigger.transform.SetParent(parent,false);trigger.transform.localPosition=new Vector3(x+2.3f,end+1,1.4f);var c=trigger.AddComponent<BoxCollider>();c.isTrigger=true;c.size=new Vector3(lane,2,.8f);var t=trigger.AddComponent<StairClimbTrigger>();t.destination=destination;t.stairID=id;
 // End guard behind transition, and mid-landing perimeter protection.
 Box("End guard",new Vector3(x+2.3f,end+.6f,.5f),new Vector3(lane,1.2f,.1f),glass);
 Box("Landing guard",new Vector3(x+1.5f,rise*direction+.6f,7),new Vector3(3,1.2f,.1f),glass);
 }
 static void Flight(float x,float z,float y,float run,float rise,float width){
 for(int i=0;i<12;i++){float t=(i+.5f)/12f;float top=y+rise*(rise>0?(i+1)/12f:i/12f);Box("Step",new Vector3(x+width/2,top-.08f,z+run*t),new Vector3(width,.16f,Mathf.Abs(run)/12),floor,false);}
 // Invisible sloped collision surface removes desktop stair bob; visible steps remain.
 var ramp=new GameObject("Smooth stair collider");ramp.transform.SetParent(parent,false);var mesh=new Mesh();mesh.vertices=new[]{new Vector3(x,y,z),new Vector3(x+width,y,z),new Vector3(x,y+rise,z+run),new Vector3(x+width,y+rise,z+run)};mesh.triangles=run>0?new[]{0,2,1,1,2,3}:new[]{0,1,2,1,3,2};mesh.RecalculateNormals();
 string meshPath=Generated+"/StairRamp_"+x.ToString(System.Globalization.CultureInfo.InvariantCulture)+"_"+run.ToString(System.Globalization.CultureInfo.InvariantCulture)+"_"+rise.ToString(System.Globalization.CultureInfo.InvariantCulture)+".asset";
 var stored=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);if(stored==null){AssetDatabase.CreateAsset(mesh,meshPath);stored=mesh;}else UnityEngine.Object.DestroyImmediate(mesh);ramp.AddComponent<MeshCollider>().sharedMesh=stored;
 foreach(float side in new[]{x-.05f,x+width+.05f}){var a=new Vector3(side,y+.65f,z);var b=new Vector3(side,y+rise+.65f,z+run);var rail=Box("Stair safety barrier",(a+b)/2,new Vector3(.08f,1.3f,(b-a).magnitude),glass);rail.transform.localRotation=Quaternion.LookRotation(b-a,Vector3.up);}
 }
 static void Lift(LiftData d,int level){
 var old=parent;var go=new GameObject("Lift_"+d.id);go.transform.SetParent(parent,false);parent=go.transform;
 float front=d.northFacing?d.z+3:d.z;float back=d.northFacing?d.z:d.z+3;
 Box("Cabin back",new Vector3(d.x+1.5f,1.5f,back),new Vector3(3,3,.12f),metal);foreach(float x in new[]{d.x,d.x+3})Box("Cabin side",new Vector3(x,1.5f,d.z+1.5f),new Vector3(.12f,3,3),metal);
 var lc=go.AddComponent<LiftController>();lc.cabinID=d.id;lc.currentLevel=level;lc.cabinCentre=new Vector3(d.x+1.5f,1.5f,d.z+1.5f);
 var doorRoot=new GameObject("Sliding doors");doorRoot.transform.SetParent(parent,false);var ld=doorRoot.AddComponent<LiftDoor>();lc.door=ld;var previous=parent;parent=doorRoot.transform;
 ld.left=Box("Left",new Vector3(d.x+.85f,1.1f,front),new Vector3(1.3f,2.2f,.1f),metal).transform;ld.right=Box("Right",new Vector3(d.x+2.15f,1.1f,front),new Vector3(1.3f,2.2f,.1f),metal).transform;parent=previous;
 float outside=d.northFacing?.2f:-.2f;var call=Box("Call button",new Vector3(d.x+.15f,1.25f,front+outside),new Vector3(.18f,.25f,.15f),accent);var ci=call.AddComponent<InteractableObject>();ci.prompt="Call lift "+d.id;ci.lift=lc;
 var panel=Box("Floor panel",new Vector3(d.x+.2f,1.35f,d.z+1.5f),new Vector3(.16f,.6f,.5f),accent);var pi=panel.AddComponent<InteractableObject>();pi.prompt="Choose lift floor (C / G / 1–8)";pi.lift=lc;pi.cabinPanel=true;
 lc.indicator=Sign(FloorConfig.Label(level),new Vector3(d.x+1.5f,2.55f,front+outside),d.northFacing?180:0,.18f);
 lc.cabinIndicator=Sign(FloorConfig.Label(level),new Vector3(d.x+1.5f,2.4f,back+(d.northFacing?.15f:-.15f)),d.northFacing?180:0,.18f);
 Spawn("lift:"+d.id,new Vector3(d.x+1.5f,.1f,d.z+1.5f),d.northFacing?0:180);
 parent=old;
 }
 static void DirectoryBoard(FloorConfig config){
 var go=Box("Floor directory",new Vector3(30.5f,1.3f,8.5f),new Vector3(.6f,1.4f,.25f),accent);var i=go.AddComponent<InteractableObject>();i.prompt="Floor directory";i.information="C: Canteen\nG: Admin / entrance / CCTV security\n1: Dean / faculty / classroom allocation provisional\n2–3: Classrooms\n4–6: Labs — assignments pending\n7: Library / AR-VR lab\n8: Classrooms\n\nCurrent floor: "+config.displayName+"\n"+string.Join("\n",config.ResolvedRooms().Select(r=>r.label+(r.isPlaceholder?" [PLACEHOLDER]":"")));
 Sign("DIRECTORY\nE to read",new Vector3(30.5f,2.3f,8.3f),0,.12f);
 }
 static void BuildCore(){
 var systems=new GameObject("Campus managers");CanteenOrderingSetup.WireCore(systems.scene);if(MissionSetup.Installed)MissionSetup.WireCore(systems.scene);var tm=systems.AddComponent<SceneTransitionManager>();var hud=systems.AddComponent<DesktopHUD>();
 var player=new GameObject("Desktop player — replace with XR Origin in XR milestone");var cc=player.AddComponent<CharacterController>();cc.height=1.75f;cc.radius=.25f;cc.center=new Vector3(0,.9f,0);cc.stepOffset=.25f;cc.slopeLimit=45;var pc=player.AddComponent<DesktopPlayerController>();
 var camera=new GameObject("Main Camera");camera.tag="MainCamera";camera.transform.SetParent(player.transform,false);camera.transform.localPosition=new Vector3(0,1.65f,0);pc.view=camera.AddComponent<Camera>();pc.view.nearClipPlane=.05f;pc.view.farClipPlane=90;camera.AddComponent<AudioListener>();tm.player=pc;hud.player=pc;
 var sun=new GameObject("Preview lighting — bake later");var light=sun.AddComponent<Light>();light.type=LightType.Directional;light.intensity=.8f;light.shadows=LightShadows.None;sun.transform.rotation=Quaternion.Euler(50,-30,0);
 }
 [MenuItem("Campus Quest/3. Review ground floor from above")]
 static void ReviewGround(){ReviewLevel(0);}
 [MenuItem("Campus Quest/4. Review typical floor (Floor 2)")]
 static void ReviewTypical(){ReviewLevel(2);}
 [MenuItem("Campus Quest/5. Review library floor (Floor 7)")]
 static void ReviewLibrary(){ReviewLevel(7);}
 static void ReviewLevel(int level){
 if(EditorApplication.isPlaying){Debug.LogWarning("Exit Play mode first.");return;}
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 var scene=EditorSceneManager.OpenScene(Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity",OpenSceneMode.Single);
 foreach(var root in scene.GetRootGameObjects())foreach(var child in root.GetComponentsInChildren<Transform>(true))
  if(child.name=="Ceiling"||(child.name.StartsWith("Batched_Active_")&&child.name.EndsWith("_V_Ceiling")))SceneVisibilityManager.instance.Hide(child.gameObject,false);
 Selection.activeObject=null;
 var view=EditorWindow.GetWindow<SceneView>();
 view.LookAt(new Vector3(20,0,28),Quaternion.Euler(90,0,0),34,true,true);
 view.Repaint();
 }
 [MenuItem("Campus Quest/2. Validate generated scenes")]
 static void Validate(){
 if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;int errors=0;
 for(int level=-1;level<=8;level++){
 var path=Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity";if(!File.Exists(path)){Debug.LogError("Missing "+path);errors++;continue;}
 var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);var roots=scene.GetRootGameObjects();var env=roots.Select(r=>r.GetComponent<EnvironmentRoot>()).FirstOrDefault(r=>r!=null);
 if(env==null||(level!=-1&&env.config.ResolvedRooms().Count(r=>r.id=="washroom_rear")!=1)){Debug.LogError("Rear washroom missing or duplicated: "+path);errors++;}
 if(level==7&&env!=null){var library=env.config.ResolvedRooms().FirstOrDefault(r=>r.id=="library");if(library==null||library.width!=8||library.depth!=14||env.config.ResolvedRooms().Any(r=>r.id=="right_0"||r.id=="right_1")){Debug.LogError("Two-room library override incorrect");errors++;}}
 var spawns=roots.SelectMany(r=>r.GetComponentsInChildren<SpawnPoint>(true)).ToArray();
 foreach(var id in (level==-1?new[]{"start","stair:FrontLeft","stair:FrontRight"}:new[]{"start","stair:FrontLeft","stair:FrontRight","stair:Rear"}).Concat(Enumerable.Range(1,10).Select(i=>"lift:"+i)))if(spawns.Count(s=>s.id==id)!=1){Debug.LogError(path+" missing/duplicate spawn "+id);errors++;}
 int expected=level==-1?2:level==0?5:level==8?3:6;int triggers=roots.Sum(r=>r.GetComponentsInChildren<StairClimbTrigger>().Length);if(triggers!=expected){Debug.LogError("Stair trigger count "+path);errors++;}
 foreach(var t in roots.SelectMany(r=>r.GetComponentsInChildren<StairClimbTrigger>()))if((t.destination==-1&&t.stairID=="Rear")||t.destination< -1||t.destination>8||Mathf.Abs(t.destination-level)!=1){Debug.LogError("Invalid stair destination");errors++;}
 foreach(var l in roots.SelectMany(r=>r.GetComponentsInChildren<LiftController>()))if(l.door==null||l.indicator==null||l.currentLevel!=level){Debug.LogError("Unwired lift");errors++;}
 }
 EditorSceneManager.OpenScene(Generated+"/Scenes/Core.unity",OpenSceneMode.Single);Debug.Log("Campus structural validation completed. Errors: "+errors+". This does not test playability, XR, lighting or performance.");
 }
}
}
#endif
