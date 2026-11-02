#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace CampusQuest.Editor {
public static class MissionSetup {
 const string Root="Assets/CampusQuest/Generated",Data=Root+"/Missions";
 const string Rig="Orientation checkpoints";
 public static bool Installed=>AssetDatabase.LoadAssetAtPath<MissionData>(Data+"/Mission_0.asset")!=null;
 static T Asset<T>(string file,Action<T> init)where T:ScriptableObject {
 var a=AssetDatabase.LoadAssetAtPath<T>(Data+"/"+file+".asset");if(a!=null)return a;
 a=ScriptableObject.CreateInstance<T>();init(a);AssetDatabase.CreateAsset(a,Data+"/"+file+".asset");return a;
 }
 static T Component<T>(GameObject go)where T:UnityEngine.Component {return go.GetComponent<T>()??go.AddComponent<T>();}
 static FloorConfig Floor(int level){var f=AssetDatabase.LoadAssetAtPath<FloorConfig>(Root+"/Configs/Floor_"+level+".asset");if(f==null)throw new InvalidOperationException("Missing generated FloorConfig "+level);return f;}
 static FacilityData Facility(string id,int level,string room,string name,bool verified,string text){return Asset<FacilityData>("Facility_"+id,f=>{f.facilityID=id;f.floor=Floor(level);f.roomID=room;f.fallbackName=name;f.isVerified=verified;f.isPlaceholder=!verified;f.learningText=text;});}
 static QuizQuestionData Question(string id,FacilityData f,string prompt,string[] answers,int correct,string explanation){return Asset<QuizQuestionData>("Quiz_"+id,q=>{q.questionID=id;q.facility=f;q.prompt=prompt;q.answers=answers;q.correctIndex=correct;q.explanation=explanation;q.isVerified=true;q.isPlaceholder=false;});}
 static MissionObjective Visit(FacilityData f,string text="Read the information panel")=>new MissionObjective{facility=f,instruction=text};
 static MissionObjective Event(string id,string text)=>new MissionObjective{eventID=id,instruction=text};
 static MissionData Mission(int i,string title,int seconds,string briefing,string hint,int[] floors,MissionObjective[] objectives,QuizQuestionData question=null,bool final=false){return Asset<MissionData>("Mission_"+i,m=>{m.missionID="mission_"+i;m.title=title;m.budgetSeconds=seconds;m.briefing=briefing;m.hint=hint;m.allowedFloors=floors;m.objectives=objectives;m.question=question;m.finalQuiz=final;});}
 public static void WireCore(Scene scene){
 Directory.CreateDirectory(Data);AssetDatabase.Refresh();
 var entrance=Facility("entrance",0,"","Main entrance",true,"The main entrance faces the front lift bank. Use the lift lobby and the two front staircases as landmarks.");
 var admin=Facility("admin",0,"inner_north_R","Admin / Reception",true,"Admin and reception share this office. General orientation guidance: begin here when you need direction to administrative support. Specific campus procedures and opening hours still need confirmation.");
 var security=Facility("security",0,"security","CCTV Security Room",true,"The CCTV security room is immediately on your left when entering. It is where the campus CCTV feeds are connected. This panel identifies the room; it does not authorize access to CCTV equipment.");
 var classroom=Facility("classroom",1,"left_0","Sample classroom",false,"Use this sample timetable to practise finding a classroom. Room 101 follows the supplied numbering example; its exact assignment is provisional.");
 var dean=Facility("dean",1,"front_corner_right","Dean's office",true,"The dean's office is on Floor 1. General academic guidance: use faculty support for routine course questions; the dean's office handles senior academic matters. Confirm local appointment and escalation procedures with campus staff.");
 var faculty=Facility("faculty",1,"faculty_east","Faculty area",true,"Faculty areas are at the back of the floor beside the rear washrooms. General guidance: seek course-related academic help from the appropriate faculty member; availability needs confirmation.");
 var library=Facility("library",7,"library","Library",true,"The library is on Floor 7, combining two rooms along the left corridor when approached from the front. Libraries provide reading and study resources; local borrowing rules and hours need confirmation.");
 var arvr=Facility("arvr",7,"front_lab","AR/VR lab",false,"AR/VR labs support immersive computing and interactive experiments. The facility is on Floor 7, but this prototype's room placement is provisional. Do not learn this room position as verified campus navigation.");
 var canteen=Facility("canteen",-1,"canteen","Student canteen",true,"Choose a dish at the ordering counter, collect its coupon, hand the coupon over at the serving counter, then collect the tray. This is a simulated order with no payment.");
 var qAdmin=Question("admin_check",admin,"Which floor contains Admin / Reception?",new[]{"Ground","Floor 1","Floor 7"},0,"Admin / Reception is on Ground, behind the front lift area.");
 var qDean=Question("dean_check",dean,"Which floor contains the dean's office?",new[]{"Ground","Floor 1","Floor 7"},1,"The dean's office is on Floor 1 only.");
 var finalQuestions=new[]{
 Question("security_final",security,"Which facility is immediately on your left when entering?",new[]{"Library","CCTV Security Room","Dean's office"},1,"The CCTV security room is on your left as you enter."),
 Question("library_final",library,"Which floor contains the library?",new[]{"Floor 1","Floor 4","Floor 7"},2,"The library is on Floor 7."),
 Question("dean_final",dean,"Where should you look for the dean's office?",new[]{"Floor 1","Canteen level","Floor 8"},0,"The dean's office is on Floor 1."),
 Question("canteen_final",canteen,"What do you collect after selecting a dish?",new[]{"A coupon","A tray immediately","A library card"},0,"Collect the coupon first; hand it over at the serving counter before collecting a tray.")};
 var missions=new[]{
 Mission(0,"Welcome and campus landmarks",120,"Welcome! Practise walking and using E. Identify the entrance, Admin / Reception and CCTV security room.","Face the front lifts from the entrance. Security is on your left. Admin / Reception is behind the front lift bank.",new[]{0},new[]{Visit(entrance,"Stand at the entrance"),Visit(admin,"Identify reception"),Visit(security,"Identify security")}),
 Mission(1,"Find administrative support",120,"Read the Admin / Reception panel, then answer a short location question.","Admin and reception share one office on Ground.",new[]{0},new[]{Visit(admin)},qAdmin),
 Mission(2,"Find your sample classroom",180,"Read the sample timetable near the front lift lobby. Take stairs from Ground to Floor 1, then identify the sample classroom.","The timetable is beside the front lift lobby on Ground. Use either front staircase UP, then read the sample classroom panel on Floor 1.",new[]{0,1},new[]{Event("timetable:read","Read the sample timetable on Ground"),Event("stairs:G-1","Take stairs from Ground to Floor 1"),Visit(classroom)}),
 Mission(3,"Dean and faculty support",120,"Find the dean's office on Floor 1 and the faculty area at the rear beside the washrooms.","Dean: front corner. Faculty: back of the floor, next to the rear washrooms.",new[]{1},new[]{Visit(dean),Visit(faculty)},qDean),
 Mission(4,"Academic resources",240,"Take a lift to Floor 7. Find the library and explore the provisional AR/VR practice location.","Library: two combined rooms down the left corridor from the front, beyond the washroom. AR/VR: provisional front lab in this prototype.",new[]{1,7},new[]{Event("lift:7","Ride a lift to Floor 7"),Visit(library),Visit(arvr)}),
 Mission(5,"Order at the canteen",240,"Take a lift to C. Select food, collect the coupon, hand it over and collect the tray.","Enter the student canteen. The ordering counter and L-shaped serving counter have red interaction panels.",new[]{7,-1},new[]{Visit(canteen,"Enter the student canteen"),Event("order:complete","Complete the coupon-to-tray order")}),
 Mission(6,"Final campus check",120,"Review what you learned about confirmed facilities. The quiz uses places you visited.","Read the explanations. You may retry, or review an answer and continue without answer points.",new[]{-1},new MissionObjective[0],null,true)};
 var go=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="Orientation systems");if(go==null){go=new GameObject("Orientation systems");SceneManager.MoveGameObjectToScene(go,scene);}
 var mm=Component<MissionManager>(go);mm.score=Component<ScoreManager>(go);mm.checkpoints=Component<CheckpointManager>(go);mm.timer=Component<GameTimer>(go);mm.quiz=Component<QuizManager>(go);mm.results=Component<ResultsManager>(go);mm.guide=Component<CampusGuideController>(go);Component<MissionUI>(go);
 mm.score.rules=Asset<ScoringRules>("ScoringRules",s=>{});
 // Preserve an existing configured campaign; first installation uses supplied defaults.
 if(mm.missions.Length==0)mm.missions=missions;if(mm.facilities.Length==0)mm.facilities=new[]{entrance,admin,security,classroom,dean,faculty,library,arvr,canteen};
 if(mm.quiz.finalQuestions.Length==0)mm.quiz.finalQuestions=finalQuestions;
 EditorUtility.SetDirty(mm);EditorUtility.SetDirty(mm.score);EditorUtility.SetDirty(mm.quiz);
 }
 public static void WireEnvironment(Scene scene){
 var env=scene.GetRootGameObjects().Select(g=>g.GetComponent<EnvironmentRoot>()).FirstOrDefault(e=>e!=null);if(env==null)return;
 foreach(var t in env.GetComponentsInChildren<Transform>(true).Where(t=>t.name==Rig).ToArray())UnityEngine.Object.DestroyImmediate(t.gameObject);
 var rig=new GameObject(Rig);rig.transform.SetParent(env.transform,false);
 foreach(var guid in AssetDatabase.FindAssets("t:FacilityData",new[]{Data})){
 var f=AssetDatabase.LoadAssetAtPath<FacilityData>(AssetDatabase.GUIDToAssetPath(guid));if(f.Level!=env.config.level)continue;
 if(f.facilityID=="entrance"){Trigger(rig.transform,f,new Vector3(20,1,53),new Vector3(4,2,4));continue;}
 var room=f.Room;if(room==null){Debug.LogWarning("Missing facility room: "+f.facilityID);continue;}
 var container=env.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="SOURCE_ID_"+room.id||t.name=="PLACEHOLDER_"+room.id);
 var info=container==null?null:container.GetComponentsInChildren<InteractableObject>(true).FirstOrDefault(i=>i.name=="Information button");
 if(info==null){Debug.LogWarning("Missing information button: "+f.facilityID);continue;}
 var panel=Component<FacilityInformationPanel>(info.gameObject);panel.facility=f;info.facilityPanel=panel;
 if(f.facilityID=="canteen")Trigger(rig.transform,f,new Vector3(room.x+room.width/2,1,room.z+room.depth/2),new Vector3(room.width-.6f,2,room.depth-.6f));
 }
 if(env.config.level==0){
 var board=GameObject.CreatePrimitive(PrimitiveType.Cube);board.name="Sample timetable — PLACEHOLDER lesson";board.transform.SetParent(rig.transform,false);board.transform.localPosition=new Vector3(28,1.3f,47);board.transform.localScale=new Vector3(1.2f,1,.15f);
 board.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Campus red.mat");
 var ev=board.AddComponent<MissionEventInteractable>();ev.eventID="timetable:read";ev.title="Sample timetable — practice only";ev.body="09:00 — Introduction to campus learning. Take the stairs to Floor 1. This timetable and classroom assignment are placeholders.";ev.target=AssetDatabase.LoadAssetAtPath<FacilityData>(Data+"/Facility_classroom.asset");
 var interact=board.AddComponent<InteractableObject>();interact.missionEvent=ev;interact.prompt="Read sample timetable";
 var sign=new GameObject("Timetable label");sign.transform.SetParent(rig.transform,false);sign.transform.localPosition=new Vector3(28,2,47.1f);sign.transform.localRotation=Quaternion.Euler(0,180,0);var tm=sign.AddComponent<TextMesh>();tm.text="SAMPLE TIMETABLE\nE to read";tm.characterSize=.09f;tm.fontSize=48;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");sign.GetComponent<MeshRenderer>().sharedMaterial=tm.font.material;
 }
 }
 static void Trigger(Transform parent,FacilityData f,Vector3 position,Vector3 size){var go=new GameObject("Checkpoint_"+f.facilityID);go.transform.SetParent(parent,false);go.transform.localPosition=position;var col=go.AddComponent<BoxCollider>();col.isTrigger=true;col.size=size;go.AddComponent<MissionArrivalCheckpoint>().facility=f;}
 [MenuItem("Campus Quest/10. Install orientation missions")]
 public static void Install(){
 if(EditorApplication.isPlaying){Debug.LogWarning("Exit Play mode first.");return;}if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 var scene=EditorSceneManager.OpenScene(Root+"/Scenes/Core.unity",OpenSceneMode.Single);CanteenOrderingSetup.WireCore(scene);WireCore(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
 for(int level=-1;level<=8;level++){scene=EditorSceneManager.OpenScene(Root+"/Scenes/"+FloorConfig.SceneFor(level)+".unity",OpenSceneMode.Single);WireEnvironment(scene);EditorSceneManager.SaveScene(scene);}
 Validate();EditorSceneManager.OpenScene(Root+"/Scenes/Core.unity",OpenSceneMode.Single);Debug.Log("Orientation installed. Open Core, Play, then Start orientation.");
 }
 [MenuItem("Campus Quest/11. Validate orientation missions")]
 public static void Validate(){
 if(EditorApplication.isPlaying)return;if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 var scene=EditorSceneManager.OpenScene(Root+"/Scenes/Core.unity",OpenSceneMode.Single);var m=scene.GetRootGameObjects().Select(g=>g.GetComponent<MissionManager>()).FirstOrDefault(x=>x!=null);
 if(m==null||m.missions.Length!=7||m.score==null||m.checkpoints==null||m.timer==null||m.quiz==null||m.results==null||m.guide==null)throw new InvalidOperationException("Mission systems are not wired.");
 if(m.missions.Sum(x=>x.budgetSeconds)>1200)throw new InvalidOperationException("Mission budgets exceed 20 minutes.");
 if(m.facilities.Select(f=>f.facilityID).Distinct().Count()!=m.facilities.Length)throw new InvalidOperationException("Duplicate facility IDs.");
 var questions=m.quiz.finalQuestions.Concat(m.missions.Where(x=>x.question!=null).Select(x=>x.question));
 foreach(var q in questions)if(!q.Eligible)throw new InvalidOperationException("Unverified/placeholder or invalid quiz: "+q.name);
 foreach(var mission in m.missions)foreach(var objective in mission.objectives)if(string.IsNullOrEmpty(objective.Key))throw new InvalidOperationException("Missing objective ID.");
 if(m.facilities.Any(f=>!string.IsNullOrEmpty(f.roomID)&&f.Room==null))throw new InvalidOperationException("Facility room assignment missing.");
 MissionSystemTests.Run();
 var expected=m.facilities.Select(f=>f.facilityID).ToArray();var found=new System.Collections.Generic.HashSet<string>();bool timetable=false;
 for(int level=-1;level<=8;level++){
 var envScene=EditorSceneManager.OpenScene(Root+"/Scenes/"+FloorConfig.SceneFor(level)+".unity",OpenSceneMode.Single);
 foreach(var root in envScene.GetRootGameObjects()){
 foreach(var p in root.GetComponentsInChildren<FacilityInformationPanel>(true))if(p.facility!=null)found.Add(p.facility.facilityID);
 foreach(var p in root.GetComponentsInChildren<MissionArrivalCheckpoint>(true))if(p.facility!=null)found.Add(p.facility.facilityID);
 foreach(var p in root.GetComponentsInChildren<MissionEventInteractable>(true))if(p.eventID=="timetable:read")timetable=true;
 }}
 if(!timetable||expected.Any(id=>!found.Contains(id)))throw new InvalidOperationException("Missing scene checkpoints or timetable. Run Install orientation missions.");
 EditorSceneManager.OpenScene(Root+"/Scenes/Core.unity",OpenSceneMode.Single);
 Debug.Log("Orientation validation: Errors: 0. Seven missions / 19-minute target. Provisional practice targets are excluded from quiz. Play-test required.");
 }
}
}
#endif
