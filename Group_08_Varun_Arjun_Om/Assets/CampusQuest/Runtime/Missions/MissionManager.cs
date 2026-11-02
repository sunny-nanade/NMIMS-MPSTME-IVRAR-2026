using System.Linq;
using UnityEngine;
namespace CampusQuest {
public class MissionManager:MonoBehaviour {
 public static MissionManager Instance {get;private set;}
 public MissionData[] missions=new MissionData[0];
 public FacilityData[] facilities=new FacilityData[0];
 public ScoreManager score;public CheckpointManager checkpoints;public GameTimer timer;public QuizManager quiz;public ResultsManager results;public CampusGuideController guide;
 public bool Started {get;private set;}
 public bool Finished {get;private set;}
 public bool DebugUsed {get;private set;}
 public int CompletedCount {get;private set;}
 public int Mistakes {get;private set;}
 int missionMistakes;bool checking;
 public MissionData Current=>CompletedCount<missions.Length?missions[CompletedCount]:null;
 public bool Visited(FacilityData f)=>f!=null&&checkpoints.Has("visit:"+f.facilityID);
 void Awake(){Instance=this;}
 public void StartRun(){if(Started)return;Started=true;Announce();}
 void Update(){
 var tm=SceneTransitionManager.Instance;var hud=DesktopHUD.Instance;
 if(!Started||Finished||tm==null||tm.Busy)return;
 if(hud!=null&&hud.IsPaused)return;
 timer.Tick(Time.unscaledDeltaTime);
 if(timer.Remaining<=0){Finish(false,"The 30-minute active-play limit was reached. Restart to practise unfinished facilities.");return;}
 var order=CanteenOrderManager.Instance;
 if(order!=null&&order.Order.RewardGranted)Signal("order:complete");
 CheckProgress();
 }
 public void Signal(string key){if(!Started||Finished)return;checkpoints.Mark(key);}
 public void Visit(FacilityData f){
 if(f==null||!Started||Finished)return;
 var tm=SceneTransitionManager.Instance;if(tm==null||tm.Busy||tm.CurrentLevel!=f.Level)return;
 if(checkpoints.Mark("visit:"+f.facilityID)){score.Award("facility:"+f.facilityID,score.rules.facility);guide.Say(f.DisplayName+" found. "+(f.Verified?"Read the facility panel to learn more.":"This is a provisional practice location."));}
 }
 public void Arrived(int from,int to,string spawn){
 if(!Started||Finished)return;
 if(spawn.StartsWith("stair:")&&from==0&&to==1)Signal("stairs:G-1");
 if(spawn.StartsWith("lift:")&&to==7)Signal("lift:7");
 if(Current!=null&&Current.allowedFloors.Length>0&&!Current.allowedFloors.Contains(to)){score.Penalty(score.rules.wrongFloor);Mistake();guide.Say("This mission is on another floor. "+Current.hint);}
 }
 public void Mistake(){Mistakes++;missionMistakes++;if(Mistakes>=2&&Current!=null)guide.Say(Current.hint);}
 void CheckProgress(){
 if(checking||Current==null||quiz.Active)return;checking=true;
 var current=Current;
 if(current.objectives.All(o=>checkpoints.Has(o.Key))){
 if(current.question!=null&&!checkpoints.Has("quiz:"+current.question.questionID)){
  if(DesktopHUD.Instance==null||!DesktopHUD.Instance.LocalMenuOpen){if(!current.question.Eligible){Signal("quiz:"+current.question.questionID);guide.Say("This question was omitted because its data is unverified. No answer points awarded.");}else if(!quiz.Begin(new[]{current.question},false))guide.Say("Visit the question’s facility before answering.");}
 }else if(current.finalQuiz&&!checkpoints.Has("quiz:final")){
  if(!quiz.Begin(quiz.finalQuestions,true))Finish(false,"No verified, visited quiz questions are available. Check the quiz data.");
 }else CompleteCurrent();
 }
 checking=false;
 }
 void CompleteCurrent(){
 score.Award("mission:"+Current.missionID,score.rules.checkpoint);
 if(missionMistakes==0)score.Award("route:"+Current.missionID,score.rules.efficientRoute);
 CompletedCount++;missionMistakes=0;
 if(CompletedCount>=missions.Length)Finish(true,"All ten missions completed. Review the answers and remaining practice locations below.");else Announce();
 }
 void Announce(){if(Current!=null)guide.Say(Current.briefing);}
 public void DebugSkip(){if(!Started||Finished||Current==null)return;DebugUsed=true;quiz.Cancel();CompletedCount++;missionMistakes=0;if(Current==null)Finish(false,"Debug run completed; skipped missions do not earn the badge.");else Announce();}
 public void MarkDebug(){DebugUsed=true;}
 void Finish(bool complete,string reason){Finished=true;quiz.Cancel();results.Show(complete,reason);if(DesktopHUD.Instance!=null)DesktopHUD.Instance.CloseMenus();}
 public void Restart(){
 Started=false;Finished=false;DebugUsed=false;CompletedCount=0;Mistakes=0;missionMistakes=0;checking=false;
 score.ResetSession();checkpoints.ResetSession();timer.ResetSession();quiz.ResetSession();results.ResetSession();
 if(CanteenOrderManager.Instance!=null)CanteenOrderManager.Instance.ResetSession();
 if(CampusServicesManager.Instance!=null)CampusServicesManager.Instance.ResetSession();
 if(DesktopHUD.Instance!=null)DesktopHUD.Instance.CloseMenus();
 if(SceneTransitionManager.Instance!=null)SceneTransitionManager.Instance.Travel(0,"start");
 }
 void OnDestroy(){if(Instance==this)Instance=null;}
}
}
