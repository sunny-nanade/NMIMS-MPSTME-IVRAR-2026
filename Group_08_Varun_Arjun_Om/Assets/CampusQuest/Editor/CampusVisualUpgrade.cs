#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using A=CampusQuest.Editor.CampusVisualAssets;
namespace CampusQuest.Editor {
public static class CampusVisualUpgrade {
 const string Generated="Assets/CampusQuest/Generated",DetailRoot="Campus_Detail_Pass";
 public static bool Installed=>AssetDatabase.LoadAssetAtPath<CampusVisualSettings>(A.Root+"/Settings.asset")!=null;
 static CampusVisualSettings settings;
 static void Prepare(){
 Directory.CreateDirectory(A.Root);AssetDatabase.Refresh();settings=AssetDatabase.LoadAssetAtPath<CampusVisualSettings>(A.Root+"/Settings.asset");
 if(settings==null){settings=ScriptableObject.CreateInstance<CampusVisualSettings>();AssetDatabase.CreateAsset(settings,A.Root+"/Settings.asset");}
 if(A.Wall==null)A.Prepare(settings.textureResolution);
 }
 static GameObject Group(Transform parent,string name,Vector3 position,Quaternion rotation){var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localRotation=rotation;return g;}
 public static void Decorate(EnvironmentRoot env){
 if(env==null)return;Prepare();
 var old=env.transform.Find(DetailRoot);if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
 foreach(var t in env.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="CampusLiftSkin").ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
 var detail=Group(env.transform,DetailRoot,Vector3.zero,Quaternion.identity).transform;
 CutCeilings(env);
 foreach(var mr in env.GetComponentsInChildren<MeshRenderer>(true)){
 if(!mr.gameObject.activeInHierarchy||mr.GetComponent<TextMesh>()!=null)continue;
 mr.enabled=true;
 string n=mr.gameObject.name;
 if(n=="Atrium guard"){mr.enabled=false;continue;} // Retain the original safety collider.
 if(n=="Wall"||n=="Door lintel")mr.sharedMaterial=A.Wall;
 else if(n=="Floor"||n=="Entry porch"||n=="Step"||n.Contains("landing"))mr.sharedMaterial=A.Tile;
 else if(n=="Ceiling")mr.sharedMaterial=A.Ceiling;
 else if(n.StartsWith("Serving counter")||n.StartsWith("Order counter")||n=="Kiosk APPROXIMATE")mr.sharedMaterial=A.Wood;
 else if(n=="Cabin back"||n=="Cabin side"||n=="Left"||n=="Right")mr.sharedMaterial=A.Steel;
 }
 var originalKinds=new[]{"Desk","Bench","Bookshelf","LabBench","Washbasin","Counter"};
 foreach(var t in env.GetComponentsInChildren<Transform>(true).Where(t=>originalKinds.Any(k=>t.name==k+"_APPROXIMATE")).ToArray()){
 string kind=t.name.Replace("_APPROXIMATE","");
 var room=env.config.ResolvedRooms().FirstOrDefault(r=>t.parent.name=="SOURCE_ID_"+r.id||t.parent.name=="PLACEHOLDER_"+r.id);
 string replacement=kind;
 if(kind=="Desk")replacement=env.config.level==-1?"LabBench":room!=null&&room.id=="security"?"SecurityDesk":room!=null&&room.category=="Classroom"?"ClassroomDesk":"OfficeDesk";
 if(kind=="LabBench")replacement=settings.addComputersToGenericLabs?"ComputerDesk":"LabBench";
 A.Kit(replacement,detail,env.transform.InverseTransformPoint(t.position),Quaternion.Inverse(env.transform.rotation)*t.rotation);t.gameObject.SetActive(false);if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
 }
 foreach(var room in env.config.ResolvedRooms()){
 var container=env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="SOURCE_ID_"+room.id||t.name=="PLACEHOLDER_"+room.id);
 if(container==null)continue;
 foreach(var tm in container.GetComponentsInChildren<TextMesh>(true))if(tm.name.StartsWith("Sign_"))tm.gameObject.SetActive(false);
 if(string.IsNullOrEmpty(room.doorSide)){MountedBoard(detail,"RESTRICTED AREA",new Vector3(room.Bounds.center.x,1.7f,room.Bounds.yMax+.12f),Vector3.forward,new Vector2(1.5f,.45f),A.Red,.06f);continue;}
 foreach(float offset in new[]{room.doorOffset}.Concat(room.extraDoorOffsets??new float[0]))Door(detail,room,offset);
 int extraIndex=0;
 foreach(var opening in (room.additionalDoors??new AdditionalDoorData[0])){
 var additional=JsonUtility.FromJson<RoomData>(JsonUtility.ToJson(room));
 additional.id=room.id+"_additional_"+(extraIndex++);additional.doorSide=opening.side;additional.doorOffset=opening.offset;additional.doorWidth=opening.width;
 Door(detail,additional,opening.offset);
 }

 Plaque(env,detail,container,room);
 if(room.category!="Canteen"&&!container.GetComponentsInChildren<Transform>(true).Any(t=>originalKinds.Any(k=>t.name==k+"_APPROXIMATE"))){
 string kit=room.category=="Lab"?(settings.addComputersToGenericLabs?"ComputerDesk":"LabBench"):room.category=="Classroom"?"ClassroomDesk":room.category=="Washroom"?"Washbasin":"OfficeDesk";
 float z=room.doorSide=="N"||room.doorSide=="S"?room.Bounds.center.y:room.z+1.5f;
 foreach(float x in new[]{room.x+2,room.x+room.width-2})A.Kit(kit,detail,new Vector3(x,0,z),Quaternion.identity);
 }
 if(env.config.level==7&&room.id=="front_lab"){
 var equipment=Group(detail,"VR practice prop — PLACEHOLDER",new Vector3(room.x+2,.93f,room.Bounds.center.y),Quaternion.identity).transform;
 A.Box(equipment,"Headset body",Vector3.zero,new Vector3(.26f,.14f,.16f),A.Dark);
 foreach(float x in new[]{-.065f,.065f})A.Box(equipment,"Lens",new Vector3(x,0,-.09f),new Vector3(.05f,.05f,.018f),A.Blue);
 A.Box(equipment,"Head strap",new Vector3(0,.05f,.1f),new Vector3(.22f,.025f,.03f),A.Dark);
 }
 FurnishRoom(detail,room);
 }
 if(env.config.level==-1){
 A.Box(detail,"Serving counter metal top",new Vector3(12.2f,1.065f,23),new Vector3(.98f,.05f,16),A.Steel);
 A.Box(detail,"Serving return metal top",new Vector3(14.3f,1.065f,14.7f),new Vector3(5.1f,.05f,.98f),A.Steel);
 A.Box(detail,"Ordering counter metal top",new Vector3(21,1.065f,14.7f),new Vector3(3,.05f,.98f),A.Steel);
 }
 if(env.config.ReadLayout().hasAtrium)Rails(detail,env.config.ReadLayout().atrium.Bounds);
 // Atrium-facing plaster finish behind the rear bank; lobby doors stay accessible.
 var rearBank=env.config.ReadLayout().lifts.Where(l=>!l.northFacing).ToArray();
 if(rearBank.Length>0){
 float rearZ=rearBank.Max(l=>l.z+3)+.13f;
 float height=env.config.ReadLayout().floorHeight;
 A.Box(detail,"Rear lift atrium wall",new Vector3(20,height/2,rearZ),new Vector3(18,height,.12f),A.Wall,true);
 }
 foreach(var lift in env.GetComponentsInChildren<LiftController>())DressLift(lift);
 // Visual fixture only; baked illumination is a separate pass, avoiding many realtime lights.
 foreach(var room in env.config.ResolvedRooms()){
 if(room.category=="Restricted")continue;var point=new Vector3(room.Bounds.center.x,3.01f,room.Bounds.center.y);
 if(env.config.level==-1&&env.config.ReadLayout().ceilingOpenings.Any(h=>h.Bounds.Contains(new Vector2(point.x,point.z))))continue;
 A.Box(detail,"LED fixture frame",point,new Vector3(1.2f,.08f,.35f),A.Dark);
 A.Box(detail,"LED diffuser",point-Vector3.up*.045f,new Vector3(1.12f,.025f,.29f),A.White);
 }
 LevelMarkers(detail,env.config.level,env.config.ReadLayout().hasAtrium?env.config.ReadLayout().atrium.Bounds:new Rect(11,13,18,22));
 CampusSignCleanup.Apply(env.gameObject);
 Combine(env.GetComponentsInChildren<MeshFilter>(true).Where(StaticCandidate).ToArray(),detail,"Active_"+env.config.level,true);
 }
 static bool StaticCandidate(MeshFilter f){
 return f.gameObject.activeInHierarchy&&f.GetComponent<MeshRenderer>()!=null&&f.GetComponent<MeshRenderer>().enabled&&f.sharedMesh!=null&&f.GetComponent<TextMesh>()==null&&f.GetComponentInParent<LiftDoor>()==null&&(f.GetComponentInParent<HingedCampusDoor>()==null||!f.transform.IsChildOf(f.GetComponentInParent<HingedCampusDoor>().hinge))&&f.GetComponentInParent<InteractableObject>()==null&&f.GetComponentInParent<Coupon>()==null&&f.GetComponentInParent<FoodTray>()==null;
 }
 static Vector3 Normal(string side)=>side=="N"?Vector3.forward:side=="S"?Vector3.back:side=="E"?Vector3.right:Vector3.left;
 static Vector3 DoorPoint(RoomData r,float offset){var b=r.Bounds;return r.doorSide=="N"?new Vector3(Mathf.Lerp(b.xMin,b.xMax,offset),0,b.yMax):r.doorSide=="S"?new Vector3(Mathf.Lerp(b.xMin,b.xMax,offset),0,b.yMin):r.doorSide=="E"?new Vector3(b.xMax,0,Mathf.Lerp(b.yMin,b.yMax,offset)):new Vector3(b.xMin,0,Mathf.Lerp(b.yMin,b.yMax,offset));}
 static void Door(Transform parent,RoomData room,float offset){
 Vector3 n=Normal(room.doorSide);var root=Group(parent,"RoomDoor_"+room.id,DoorPoint(room,offset),Quaternion.LookRotation(n));float width=Mathf.Max(.7f,room.doorWidth-.10f);
 foreach(float x in new[]{-room.doorWidth/2,room.doorWidth/2})A.Box(root.transform,"Door frame",new Vector3(x,1.05f,0),new Vector3(.07f,2.1f,.14f),A.Steel);
 A.Box(root.transform,"Door header",new Vector3(0,2.1f,0),new Vector3(room.doorWidth+.08f,.08f,.14f),A.Steel);
 var pivot=Group(root.transform,"Hinge",new Vector3(-width/2,0,0),Quaternion.identity);
 var leaf=A.Box(pivot.transform,"Door leaf",new Vector3(width/2,1.02f,0),new Vector3(width,2.04f,.055f),A.Wood,true);
 foreach(float z in new[]{-.035f,.035f}){
 A.Box(pivot.transform,"Vision panel frame",new Vector3(width*.5f,1.45f,z),new Vector3(width*.4f,.55f,.015f),A.Dark);
 A.Box(pivot.transform,"Vision insert — opaque approximation",new Vector3(width*.5f,1.45f,z*1.25f),new Vector3(width*.34f,.48f,.008f),A.Blue);
 A.Box(pivot.transform,"Lever handle",new Vector3(width*.83f,1,z*2),new Vector3(.16f,.035f,.055f),A.Steel);
 A.Box(pivot.transform,"Kick plate",new Vector3(width*.5f,.15f,z),new Vector3(width-.06f,.22f,.012f),A.Steel);
 }
 var door=root.AddComponent<HingedCampusDoor>();door.hinge=pivot.transform;door.leafCollider=leaf.GetComponent<BoxCollider>();door.width=width;door.openOnStart=settings.doorsStartOpen;
 pivot.transform.localRotation=Quaternion.Euler(0,door.openOnStart?door.openAngle:0,0);
 var use=leaf.AddComponent<InteractableObject>();use.campusDoor=door;use.prompt="Open / close "+room.label+" door";
 Combine(pivot.GetComponentsInChildren<MeshFilter>(),pivot.transform,"Door_"+parent.GetComponentInParent<EnvironmentRoot>().config.level+"_"+room.id+"_"+offset.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),true);
 }
 static void Plaque(EnvironmentRoot env,Transform detail,Transform roomContainer,RoomData room){
 var n=Normal(room.doorSide);var rot=Quaternion.LookRotation(n);var tangent=rot*Vector3.right;
 var pos=DoorPoint(room,room.doorOffset)+tangent*(room.doorWidth*.5f+.85f)+n*.13f+Vector3.up*1.65f;
 var info=roomContainer.GetComponentsInChildren<InteractableObject>(true).FirstOrDefault(i=>i.name=="Information button");
 if(info!=null){info.transform.position=env.transform.TransformPoint(pos);info.transform.rotation=env.transform.rotation*rot;info.transform.localScale=new Vector3(1.45f,.62f,.06f);info.GetComponent<MeshRenderer>().sharedMaterial=A.Blue;}
 else A.Box(Group(detail,"Room plaque",pos,rot).transform,"Plaque",Vector3.zero,new Vector3(1.45f,.62f,.06f),A.Blue);
 var labelRoot=Group(detail,"Mounted_room_name_"+room.id,pos,rot).transform;
 string clean=room.label.Split(new[]{" — "},StringSplitOptions.None)[0];string label=Wrap(clean,20)+(room.isPlaceholder?"\nPRACTICE":"");
 A.Label(labelRoot,label,new Vector3(0,0,.045f),.052f);
 }
 static string Wrap(string value,int count){var line="";var all="";foreach(var word in value.Split(' ')){if(line.Length+word.Length+1>count){all+=line+"\n";line="";}line+=(line.Length==0?"":" ")+word;}return all+line;}
 static void MountedBoard(Transform parent,string text,Vector3 p,Vector3 normal,Vector2 size,Material mat,float textSize=.055f){var root=Group(parent,"Mounted notice / label",p,Quaternion.LookRotation(normal)).transform;A.Box(root,"Board backing",Vector3.zero,new Vector3(size.x,size.y,.065f),A.Dark);A.Box(root,"Board face",new Vector3(0,0,.04f),new Vector3(size.x-.08f,size.y-.08f,.012f),mat);A.Label(root,text,new Vector3(0,0,.052f),textSize);}
 static void FurnishRoom(Transform detail,RoomData r){
 var b=r.Bounds;
 if(settings.showSamplePosters&&(r.id=="left_1"||r.category=="Library"||r.id=="canteen")){
 var n=Normal(r.doorSide);var rotation=Quaternion.LookRotation(n);var tangent=rotation*Vector3.right;
 var board=Group(detail,"Corridor noticeboard — SAMPLE CONTENT",DoorPoint(r,r.doorOffset)-tangent*(r.doorWidth/2+1.1f)+n*.12f+Vector3.up*1.65f,rotation).transform;
 A.Box(board,"Noticeboard frame",Vector3.zero,new Vector3(1.95f,1.2f,.075f),A.Wood);
 A.Box(board,"Noticeboard felt",new Vector3(0,0,.045f),new Vector3(1.83f,1.08f,.015f),A.Green);
 foreach(float x in new[]{-.55f,0,.55f})A.Box(board,"Sample flyer",new Vector3(x,.08f,.061f),new Vector3(.46f,.64f,.008f),A.Poster);
 A.Label(board,"SAMPLE STUDENT NOTICES",new Vector3(0,-.44f,.07f),.05f);
 }
 if(r.category=="Classroom"||r.category=="Lab"||r.category=="Faculty"||r.category=="Office"){
 bool south=r.doorSide=="N";var n=south?Vector3.forward:Vector3.back;var p=new Vector3(b.center.x,1.65f,south?b.yMin+.12f:b.yMax-.12f);
 MountedBoard(detail,r.category=="Lab"?"LAB WORKSPACE\nEquipment: placeholder":"COURSE NOTICEBOARD\nSample content",p,n,new Vector2(Mathf.Min(2.8f,b.width-1),1.1f),r.category=="Lab"?A.Blue:A.Green,.065f);
 if(settings.showSamplePosters&&b.width>=7){var poster=Group(detail,"Sample campus poster — fictional",p+Vector3.right*(b.width*.32f),Quaternion.LookRotation(n)).transform;
 A.Box(poster,"Poster paper",Vector3.zero,new Vector3(.75f,1.05f,.015f),A.Poster);
 A.Label(poster,"STUDENT\nPROJECTS\n\nSAMPLE POSTER",new Vector3(0,0,.015f),.045f);}
 }
 if(r.category=="Library"){
 A.Kit("OfficeDesk",detail,new Vector3(b.center.x,0,b.yMin+1.3f),Quaternion.identity);
 MountedBoard(detail,"LIBRARY\nRead · Study · Explore",new Vector3(b.center.x,1.8f,b.yMax-.12f),Vector3.back,new Vector2(2.4f,.9f),A.Blue,.07f);
 }
 }
 static void Rails(Transform parent,Rect a){
 foreach(float z in new[]{a.yMin,a.yMax})A.Box(parent,"Atrium fascia",new Vector3(a.center.x,-.3f,z),new Vector3(a.width,.6f,.1f),A.Wall);
 foreach(float x in new[]{a.xMin,a.xMax})A.Box(parent,"Atrium fascia",new Vector3(x,-.3f,a.center.y),new Vector3(.1f,.6f,a.height),A.Wall);
 foreach(float z in new[]{a.yMin,a.yMax}){
 A.Box(parent,"Atrium rail top",new Vector3(a.center.x,1.15f,z),new Vector3(a.width,.055f,.055f),A.Steel);
 A.Box(parent,"Atrium rail lower",new Vector3(a.center.x,.25f,z),new Vector3(a.width,.04f,.04f),A.Steel);
 for(float x=a.xMin;x<=a.xMax+.01f;x+=.6f)A.Box(parent,"Atrium rail post",new Vector3(x,.68f,z),new Vector3(.035f,1.1f,.035f),A.Steel);
 }
 foreach(float x in new[]{a.xMin,a.xMax}){
 A.Box(parent,"Atrium rail top",new Vector3(x,1.15f,a.center.y),new Vector3(.055f,.055f,a.height),A.Steel);
 A.Box(parent,"Atrium rail lower",new Vector3(x,.25f,a.center.y),new Vector3(.04f,.04f,a.height),A.Steel);
 for(float z=a.yMin;z<=a.yMax+.01f;z+=.6f)A.Box(parent,"Atrium rail post",new Vector3(x,.68f,z),new Vector3(.035f,1.1f,.035f),A.Steel);
 }
 }
 static void LevelMarkers(Transform parent,int level,Rect a){
 MountedBoard(parent,"LEVEL "+FloorConfig.Label(level),new Vector3(a.xMin+.05f,-.32f,a.center.y),Vector3.right,new Vector2(1.5f,.55f),A.Blue,.075f);
 MountedBoard(parent,"LEVEL "+FloorConfig.Label(level),new Vector3(a.xMax-.05f,-.32f,a.center.y),Vector3.left,new Vector2(1.5f,.55f),A.Blue,.075f);
 }
 static void DressLift(LiftController lift){
 var data=lift.GetComponentInParent<EnvironmentRoot>().config.ReadLayout().lifts.First(l=>l.id==lift.cabinID);
 float front=data.northFacing?data.z+3:data.z,back=data.northFacing?data.z:data.z+3,sign=data.northFacing?1:-1;var p=Group(lift.transform,"CampusLiftSkin",Vector3.zero,Quaternion.identity).transform;
 foreach(float x in new[]{data.x+.08f,data.x+2.92f})A.Box(p,"Lift jamb",new Vector3(x,1.15f,front+sign*.07f),new Vector3(.13f,2.3f,.2f),A.Dark);
 A.Box(p,"Lift header",new Vector3(data.x+1.5f,2.34f,front),new Vector3(3,.18f,.23f),A.Steel);
 A.Box(p,"Lift sill",new Vector3(data.x+1.5f,.015f,front),new Vector3(2.8f,.03f,.28f),A.Steel);
 A.Box(p,"Lift display surround",new Vector3(data.x+1.5f,2.55f,front+sign*.13f),new Vector3(.8f,.32f,.08f),A.Dark);
 if(lift.indicator!=null){lift.indicator.color=Color.white;lift.indicator.characterSize=.12f;}
 A.Box(p,"Cabin ceiling",new Vector3(data.x+1.5f,2.88f,data.z+1.5f),new Vector3(2.85f,.08f,2.85f),A.Dark);
 A.Box(p,"Cabin ceiling diffuser",new Vector3(data.x+1.5f,2.83f,data.z+1.5f),new Vector3(1.4f,.025f,1.4f),A.White);
 A.Box(p,"Cabin inset back panel",new Vector3(data.x+1.5f,1.5f,back+sign*.075f),new Vector3(2.4f,1.6f,.025f),A.Blue);
 A.Box(p,"Cabin handrail",new Vector3(data.x+1.5f,.95f,back+sign*.2f),new Vector3(2.4f,.045f,.055f),A.Steel);
 foreach(float x in new[]{data.x+.15f,data.x+2.85f})A.Box(p,"Side handrail",new Vector3(x,.95f,data.z+1.5f),new Vector3(.045f,.045f,2.3f),A.Steel);
 A.Box(p,"Cabin floor insert",new Vector3(data.x+1.5f,.012f,data.z+1.5f),new Vector3(2.75f,.022f,2.7f),A.Tile);
 var panel=lift.GetComponentsInChildren<InteractableObject>().FirstOrDefault(i=>i.cabinPanel);
 if(panel!=null){panel.GetComponent<Renderer>().sharedMaterial=A.Dark;
 for(int i=0;i<10;i++)A.Box(p,"Panel button detail",new Vector3(data.x+.29f,1.55f-(i/2)*.085f,data.z+1.35f+(i%2)*.2f),new Vector3(.025f,.055f,.055f),A.Steel);
 MountedBoard(p,"E\nFLOORS",new Vector3(data.x+.30f,1.85f,data.z+1.5f),Vector3.right,new Vector2(.4f,.3f),A.Dark,.045f);}
 // Moving panels retain their real collision and animation. Add strips as children, not static geometry.
 foreach(var leaf in new[]{lift.door.left,lift.door.right}){
 var skin=Group(leaf,"CampusLiftSkin",Vector3.zero,Quaternion.identity).transform;
 skin.localScale=new Vector3(1/leaf.localScale.x,1/leaf.localScale.y,1/leaf.localScale.z);
 A.Box(skin,"Panel seam",new Vector3(0,0,sign*.057f),new Vector3(.025f,2.1f,.012f),A.Dark);
 A.Box(skin,"Panel kick strip",new Vector3(0,-.85f,sign*.057f),new Vector3(1.23f,.23f,.015f),A.Steel);
 }
 }
 static IEnumerable<Rect> Subtract(Rect source,Rect hole){
 float x0=Mathf.Max(source.xMin,hole.xMin),x1=Mathf.Min(source.xMax,hole.xMax),z0=Mathf.Max(source.yMin,hole.yMin),z1=Mathf.Min(source.yMax,hole.yMax);
 if(x0>=x1||z0>=z1){yield return source;yield break;}
 if(source.xMin<x0)yield return new Rect(source.xMin,source.yMin,x0-source.xMin,source.height);
 if(x1<source.xMax)yield return new Rect(x1,source.yMin,source.xMax-x1,source.height);
 if(source.yMin<z0)yield return new Rect(x0,source.yMin,x1-x0,z0-source.yMin);
 if(z1<source.yMax)yield return new Rect(x0,z1,x1-x0,source.yMax-z1);
 }
 static void CutCeilings(EnvironmentRoot env){
 var holes=env.config.ReadLayout().ceilingOpenings??new RectData[0];if(holes.Length==0)return;
 foreach(var t in env.GetComponentsInChildren<Transform>().Where(t=>t.name=="Ceiling").ToArray()){
 var pos=t.localPosition;var size=t.localScale;var original=new Rect(pos.x-size.x/2,pos.z-size.z/2,size.x,size.z);var pieces=new List<Rect>{original};
 foreach(var h in holes)pieces=pieces.SelectMany(r=>Subtract(r,h.Bounds)).ToList();
 if(pieces.Count==1&&pieces[0]==original)continue;
 foreach(var r in pieces)A.Box(env.transform,"Ceiling",new Vector3(r.center.x,pos.y,r.center.y),new Vector3(r.width,size.y,r.height),A.Ceiling,true);
 UnityEngine.Object.DestroyImmediate(t.gameObject);
 }
 }
 static Mesh PersistMesh(string key,Mesh mesh){
 string path=A.Root+"/Meshes/"+key+".asset";var prior=AssetDatabase.LoadAssetAtPath<Mesh>(path);
 if(prior==null){mesh.name=key;AssetDatabase.CreateAsset(mesh,path);return mesh;}
 EditorUtility.CopySerialized(mesh,prior);prior.name=key;EditorUtility.SetDirty(prior);UnityEngine.Object.DestroyImmediate(mesh);return prior;
 }
 static void Combine(MeshFilter[] sources,Transform target,string prefix,bool hideSources){
 foreach(var group in sources.Where(f=>f!=null&&f.sharedMesh!=null&&f.GetComponent<MeshRenderer>()!=null&&f.GetComponent<MeshRenderer>().sharedMaterial!=null).GroupBy(f=>f.GetComponent<MeshRenderer>().sharedMaterial)){
 var combines=new List<CombineInstance>();var temps=new List<Mesh>();
 foreach(var f in group){
 var mesh=f.sharedMesh;
 if(A.Tiled(group.Key)){
 mesh=UnityEngine.Object.Instantiate(mesh);temps.Add(mesh);var vertices=mesh.vertices;var normals=mesh.normals;var uv=new Vector2[vertices.Length];
 for(int i=0;i<vertices.Length;i++){var p=f.transform.TransformPoint(vertices[i]);var n=f.transform.TransformDirection(normals[i]);float scale=group.Key==A.Tile?.833333f:1f;
 uv[i]=(Mathf.Abs(n.y)>.7f?new Vector2(p.x,p.z):Mathf.Abs(n.x)>.7f?new Vector2(p.z,p.y):new Vector2(p.x,p.y))*scale;}
 mesh.uv=uv;
 }
 combines.Add(new CombineInstance{mesh=mesh,transform=target.worldToLocalMatrix*f.transform.localToWorldMatrix});
 }
 var combined=new Mesh{indexFormat=UnityEngine.Rendering.IndexFormat.UInt32};combined.CombineMeshes(combines.ToArray(),true,true);combined.RecalculateBounds();
 string materialID=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(group.Key));
 string key=prefix+"_"+(materialID.Length>=8?materialID.Substring(0,8):"builtin")+"_"+group.Key.name.Replace(" ","_");var saved=PersistMesh(key,combined);var go=Group(target,"Batched_"+key,Vector3.zero,Quaternion.identity);go.AddComponent<MeshFilter>().sharedMesh=saved;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=group.Key;renderer.allowOcclusionWhenDynamic=false;
 if(hideSources)foreach(var f in group)f.GetComponent<MeshRenderer>().enabled=false;
 foreach(var temp in temps)UnityEngine.Object.DestroyImmediate(temp);
 }
 }
 static bool ContextCandidate(MeshFilter f,int level){
 if(!f.gameObject.activeInHierarchy||f.sharedMesh==null||f.GetComponent<MeshRenderer>()==null)return false;
 string n=f.name;
 // Only meshes are copied: no remote lift scripts, triggers or colliders.
 // Source renderers may be disabled by batching, so do not filter on enabled.
 if(f.GetComponent<TextMesh>()!=null)return false;
 if(f.GetComponentInParent<LiftController>()!=null&&!n.StartsWith("Batched_"))return true;
 if(n=="Rear lift atrium wall")return true;
 if(n=="Floor"||n=="Ceiling"||n=="Wall"||n=="Door lintel"||n.StartsWith("Atrium rail")||n=="Atrium fascia")return true;
 if(level==-1){var t=f.transform;while(t!=null){if(t.name.StartsWith("Furniture_"))return true;t=t.parent;}if(n.StartsWith("Serving counter")||n.StartsWith("Order counter"))return true;}
 return false;
 }
 public static void RefreshAtriumContext(){
 Prepare();
 var levelPaths=new List<string>();var heights=new List<float>();
 // Bake each context view into a prefab, no active gameplay objects or colliders.
 for(int level=-1;level<=8;level++){
 var scene=EditorSceneManager.OpenScene(Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity",OpenSceneMode.Single);
 var env=scene.GetRootGameObjects().Select(r=>r.GetComponent<EnvironmentRoot>()).FirstOrDefault(r=>r!=null);if(env==null)throw new InvalidOperationException("Missing floor environment "+level);
 heights.Add(env.config.ReadLayout().floorHeight);
 var shell=new GameObject("Context_Level_"+FloorConfig.Label(level));
 LevelMarkers(shell.transform,level,env.config.ReadLayout().hasAtrium?env.config.ReadLayout().atrium.Bounds:new Rect(11,13,18,22));
 CampusSignCleanup.Apply(shell);
 var markerMeshes=shell.GetComponentsInChildren<MeshFilter>();
 Combine(env.GetComponentsInChildren<MeshFilter>(true).Where(f=>ContextCandidate(f,level)).Concat(markerMeshes).ToArray(),shell.transform,"Context_"+level,false);
 foreach(var marker in markerMeshes)marker.GetComponent<MeshRenderer>().enabled=false;
 foreach(var t in shell.GetComponentsInChildren<Transform>(true))t.gameObject.layer=2;
 string path=A.Root+"/Prefabs/Context_"+level+".prefab";PrefabUtility.SaveAsPrefabAsset(shell,path);levelPaths.Add(path);UnityEngine.Object.DestroyImmediate(shell);
 }
 if(heights.Any(h=>Mathf.Abs(h-heights[0])>.001f))throw new InvalidOperationException("Context currently requires equal floor-to-floor heights.");
 var core=EditorSceneManager.OpenScene(Generated+"/Scenes/Core.unity",OpenSceneMode.Single);
 foreach(var old in core.GetRootGameObjects().Where(g=>g.GetComponent<AtriumContextController>()!=null).ToArray())UnityEngine.Object.DestroyImmediate(old);
 var tower=new GameObject("Campus atrium context — visual only");var controller=tower.AddComponent<AtriumContextController>();controller.floorHeight=heights[0];var entries=new List<AtriumContextLevel>();
 for(int i=0;i<levelPaths.Count;i++){
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(levelPaths[i]);var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab);instance.transform.SetParent(tower.transform,false);instance.transform.localPosition=Vector3.up*(i*heights[0]);PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);entries.Add(new AtriumContextLevel{level=i-1,geometry=instance});}
 controller.levels=entries.ToArray();controller.SetLevel(0);foreach(var item in entries)PrefabUtility.RecordPrefabInstancePropertyModifications(item.geometry);
 foreach(var camera in core.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>()))camera.farClipPlane=Mathf.Max(camera.farClipPlane,120);
 EditorSceneManager.SaveScene(core);AssetDatabase.SaveAssets();
 }
 [MenuItem("Campus Quest/12. Upgrade campus visuals and open atrium")]
 public static void Install(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Stop Play mode first.");return;}if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 for(int level=-1;level<=8;level++)if(!File.Exists(Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity"))throw new InvalidOperationException("Missing generated scene "+level);
 Prepare();A.BuildKit();
 for(int level=-1;level<=8;level++){
 EditorUtility.DisplayProgressBar("Campus visual upgrade","Detailing floor "+FloorConfig.Label(level),(level+1)/11f);
 try{var scene=EditorSceneManager.OpenScene(Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity",OpenSceneMode.Single);var env=scene.GetRootGameObjects().Select(g=>g.GetComponent<EnvironmentRoot>()).FirstOrDefault(g=>g!=null);Decorate(env);EditorSceneManager.SaveScene(scene);}finally{EditorUtility.ClearProgressBar();}
 }
 RefreshAtriumContext();Validate();Debug.Log("Visual upgrade applied. Open Core and Play. Verify atrium from C and upper floors, doors, signs, lifts and the mission route. Surface finishes/furniture are illustrative.");
 }
 [MenuItem("Campus Quest/13. Refresh atrium context after layout edits")]
 public static void RefreshMenu(){if(EditorApplication.isPlaying)return;if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;RefreshAtriumContext();}
 [MenuItem("Campus Quest/14. Validate visual upgrade")]
 public static void Validate(){
 if(EditorApplication.isPlaying)return;if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 var core=EditorSceneManager.OpenScene(Generated+"/Scenes/Core.unity",OpenSceneMode.Single);var c=core.GetRootGameObjects().Select(g=>g.GetComponent<AtriumContextController>()).FirstOrDefault(g=>g!=null);
 if(c==null||c.levels.Length!=10||c.levels.Select(l=>l.level).Distinct().Count()!=10)throw new InvalidOperationException("Atrium context levels missing or duplicated.");
 if(c.GetComponentsInChildren<Collider>(true).Length!=0||c.GetComponentsInChildren<InteractableObject>(true).Length!=0)throw new InvalidOperationException("Context must contain no gameplay colliders/interactions.");
 foreach(int active in Enumerable.Range(-1,10)){c.SetLevel(active);if(c.levels.Count(l=>l.geometry.activeSelf)!=9||c.levels.First(l=>l.level==active).geometry.activeSelf)throw new InvalidOperationException("Context active-floor suppression failed.");}
 c.SetLevel(0);EditorSceneManager.SaveScene(core);
 for(int level=-1;level<=8;level++){
 var scene=EditorSceneManager.OpenScene(Generated+"/Scenes/"+FloorConfig.SceneFor(level)+".unity",OpenSceneMode.Single);var env=scene.GetRootGameObjects().Select(g=>g.GetComponent<EnvironmentRoot>()).First(g=>g!=null);
 var layout=env.config.ReadLayout();
 if(level>=0){
 var hole=layout.atrium.Bounds;
 if(hole!=new Rect(11,13,18,22))throw new InvalidOperationException("Atrium footprints are not aligned on "+level);
 foreach(var room in env.config.ResolvedRooms())if(room.Bounds.Overlaps(hole))throw new InvalidOperationException("Room intrudes into atrium: "+room.id);
 foreach(var t in env.GetComponentsInChildren<Transform>().Where(t=>t.name=="Floor"||t.name=="Ceiling"||t.name=="Wall"||t.name=="Door lintel")){
 var rect=new Rect(t.localPosition.x-t.localScale.x/2,t.localPosition.z-t.localScale.z/2,t.localScale.x,t.localScale.z);
 var interior=new Rect(hole.x+.15f,hole.y+.15f,hole.width-.3f,hole.height-.3f);
 if(rect.Overlaps(interior))throw new InvalidOperationException("Geometry obstructs hollow: "+level+" "+t.name);
 }
 }else{
 foreach(var room in env.config.ResolvedRooms().Where(r=>r.id=="ac_canteen"||r.id=="faculty_canteen")){
 if(layout.ceilingOpenings.Any(h=>h.Bounds.Overlaps(room.Bounds)))throw new InvalidOperationException("Enclosed canteen overlaps opening.");
 for(float x=room.x+.25f;x<room.x+room.width;x+=.5f)for(float z=room.z+.25f;z<room.z+room.depth;z+=.5f){
 bool covered=env.GetComponentsInChildren<Transform>().Any(t=>t.name=="Ceiling"&&new Rect(t.localPosition.x-t.localScale.x/2,t.localPosition.z-t.localScale.z/2,t.localScale.x,t.localScale.z).Contains(new Vector2(x,z)));
 if(!covered)throw new InvalidOperationException("Missing roof over "+room.id);
 }
 }
 }
 if(env.transform.Find(DetailRoot)==null)throw new InvalidOperationException("Detail pass missing on "+level);
 if(env.GetComponentsInChildren<HingedCampusDoor>().Length!=env.config.ResolvedRooms().Where(r=>!string.IsNullOrEmpty(r.doorSide)).Sum(r=>1+(r.extraDoorOffsets??new float[0]).Length+(r.additionalDoors??new AdditionalDoorData[0]).Length))throw new InvalidOperationException("Door count incorrect on "+level);
 foreach(var door in env.GetComponentsInChildren<HingedCampusDoor>())if(door.hinge==null||door.leafCollider==null)throw new InvalidOperationException("Door references missing.");
 foreach(var t in env.GetComponentsInChildren<Transform>().Where(t=>t.name=="Ceiling"||(level!=-1&&t.name=="Floor"))){var rect=new Rect(t.localPosition.x-t.localScale.x/2,t.localPosition.z-t.localScale.z/2,t.localScale.x,t.localScale.z);if(rect.Contains(new Vector2(20,27)))throw new InvalidOperationException("Atrium vertical sightline blocked at floor "+level+" by "+t.name);}
 int renderers=env.GetComponentsInChildren<MeshRenderer>().Count(r=>r.enabled);int triangles=env.GetComponentsInChildren<MeshFilter>().Where(f=>f.GetComponent<MeshRenderer>()!=null&&f.GetComponent<MeshRenderer>().enabled).Sum(f=>f.sharedMesh.triangles.Length/3);
 Debug.Log("Floor "+level+" visual metrics: "+renderers+" enabled mesh renderers, "+triangles+" triangles. Not a headset performance measurement.");
 }
 EditorSceneManager.OpenScene(Generated+"/Scenes/Core.unity",OpenSceneMode.Single);Debug.Log("Visual structural checks passed. Unity Play-mode visibility, navigation and Quest performance must still be measured.");
 }
}
}
#endif
