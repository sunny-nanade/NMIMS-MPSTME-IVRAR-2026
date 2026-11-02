using System.Linq;
using UnityEngine;
namespace CampusQuest {
public class MissionUI:MonoBehaviour {
 public static MissionUI Instance {get;private set;}
 public bool ShowList;
 Vector2 scroll;
 public bool Blocking {get{var m=MissionManager.Instance;return m!=null&&(!m.Started||m.quiz.Active||m.results.Visible||ShowList);}}
 void Awake(){Instance=this;}
 public void DismissList(){ShowList=false;}
 string ObjectiveLabel(MissionObjective o)=>o.instruction+(o.facility==null?"":" — "+o.facility.DisplayName);
 void OnGUI(){
 if(XRPresentationUI.IsActive)return;
 var m=MissionManager.Instance;if(m==null)return;
 // Do not draw the mission HUD behind another modal, where it competes with reading.
 if(!Blocking){
 if(DesktopHUD.Instance!=null&&DesktopHUD.Instance.LocalMenuOpen)return;
 var current=m.Current;float width=Mathf.Min(560,(Screen.width-36)*.5f);
 string text="Mission "+(m.CompletedCount+1)+" / "+m.missions.Length+"   |   "+m.timer.RemainingText+"\nScore: "+m.score.Total;
 if(current!=null){
 var next=current.objectives.FirstOrDefault(o=>!m.checkpoints.Has(o.Key));
 text+="\n"+current.title+"\n"+(next==null?"Read the campus check to continue.":"NEXT: "+ObjectiveLabel(next));
 text+="\nEsc → Mission list and hints";
 }
 if(m.CompletedCount>=5&&!m.DebugUsed)text+="\nCampus Knowledge Badge earned";
 ReadableUI.Card(Screen.width-width-12,12,width,text,ReadableUI.Body);
 return;
 }
 var rect=ReadableUI.Modal;ReadableUI.Backdrop(rect);GUILayout.BeginArea(rect,ReadableUI.Panel);
 ReadableUI.SizeControls();scroll=GUILayout.BeginScrollView(scroll);
 if(!m.Started){
 GUILayout.Label("NMIMS Campus Quest",ReadableUI.Title);
 GUILayout.Label("Explore key facilities and complete ten orientation missions. Target: 28 minutes; maximum 30 minutes of active play.",ReadableUI.Body);
 GUILayout.Label("WASD — move\nMouse — look\nE — interact\nEsc — pause / mission list",ReadableUI.Body);
 GUILayout.Label("Look at red information buttons beside doors and press E. Begin with the entrance, reception and CCTV security room.",ReadableUI.Body);
 GUILayout.Label("Classroom 101's assignment and the AR/VR lab position are provisional practice targets, excluded from the quiz.",ReadableUI.Body);
 GUI.enabled=SceneTransitionManager.Instance!=null&&!SceneTransitionManager.Instance.Busy;
 if(ReadableUI.Button("Start orientation",rect.width)){ShowList=false;scroll=Vector2.zero;m.StartRun();}
 GUI.enabled=true;if(ReadableUI.Button("Quit",rect.width))Application.Quit();
 }else if(m.results.Visible){
 GUILayout.Label("Orientation results",ReadableUI.Title);
 GUILayout.Label(m.results.Report,ReadableUI.Body);
 if(ReadableUI.Button("Restart orientation",rect.width)){ShowList=false;scroll=Vector2.zero;m.Restart();}
 }else if(m.quiz.Active){
 var q=m.quiz.Current;if(q!=null){
 GUILayout.Label("Campus check",ReadableUI.Title);GUILayout.Space(10);GUILayout.Label(q.prompt,ReadableUI.Title);GUILayout.Space(12);
 GUI.enabled=!m.quiz.Answered;
 for(int i=0;i<q.answers.Length;i++)if(ReadableUI.Button(q.answers[i],rect.width))m.quiz.Submit(i);
 GUI.enabled=true;GUILayout.Space(10);GUILayout.Label(m.quiz.Feedback,ReadableUI.Body);
 if(m.quiz.Answered){if(ReadableUI.Button("Continue",rect.width)){scroll=Vector2.zero;m.quiz.Next();}}
 else if(ReadableUI.Button("Review answer — no answer points",rect.width))m.quiz.ReviewAnswer();
 }
 }else if(ShowList){
 GUILayout.Label("Mission list",ReadableUI.Title);
 if(m.Current!=null){GUILayout.Label("CURRENT: "+m.Current.title,ReadableUI.Title);foreach(var o in m.Current.objectives)GUILayout.Label((m.checkpoints.Has(o.Key)?"[Done] ":"[ ] ")+ObjectiveLabel(o),ReadableUI.Body);
 GUILayout.Label("Guide: "+m.guide.Subtitle,ReadableUI.Body);GUILayout.Label("Hint: "+m.Current.hint,ReadableUI.Body);}
 GUILayout.Space(12);
 for(int i=0;i<m.missions.Length;i++)GUILayout.Label((i<m.CompletedCount?"[Done] ":i==m.CompletedCount?"[Current] ":"[Later] ")+m.missions[i].title,ReadableUI.Body);
 if(ReadableUI.Button("Return to campus",rect.width))ShowList=false;
 }
 GUILayout.EndScrollView();GUILayout.EndArea();
 }
 void OnDestroy(){if(Instance==this)Instance=null;}
}
}

