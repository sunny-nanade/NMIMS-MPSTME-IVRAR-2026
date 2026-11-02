#if UNITY_EDITOR
using System;
using UnityEngine;
namespace CampusQuest.Editor {
public static class MissionSystemTests {
 static int checks;
 static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Mission test failed: "+label);}
 public static void Run(){
 checks=0;var go=new GameObject("Mission test fixture"){hideFlags=HideFlags.HideAndDontSave};
 var facility=ScriptableObject.CreateInstance<FacilityData>();var q=ScriptableObject.CreateInstance<QuizQuestionData>();var rules=ScriptableObject.CreateInstance<ScoringRules>();
 try{
 var score=go.AddComponent<ScoreManager>();score.rules=rules;
 Check(score.Award("facility:a",50)&&score.Total==50,"first facility reward");
 Check(!score.Award("facility:a",50)&&score.Total==50,"duplicate facility reward rejected");
 score.Penalty(-10);Check(score.Total==40,"wrong-floor penalty");score.Penalty(5);Check(score.Total==40,"positive penalty cannot reward");
 score.Award("invalid",-100);Check(score.Total==40,"negative award clamped");score.ResetSession();Check(score.Total==0&&score.Award("facility:a",50),"restart clears awards");
 var cp=go.AddComponent<CheckpointManager>();Check(cp.Mark("stairs:G-1")&&cp.Has("stairs:G-1"),"stair checkpoint");Check(!cp.Mark("stairs:G-1"),"duplicate checkpoint");Check(!cp.Has("lift:7"),"unvisited route not granted");cp.ResetSession();Check(!cp.Has("stairs:G-1"),"checkpoint reset");
 var timer=go.AddComponent<GameTimer>();timer.limitSeconds=5000;timer.Tick(1199);Check(timer.Remaining==1,"20-minute cap");timer.Tick(3);Check(timer.Remaining==0,"timer expires");timer.ResetSession();Check(timer.Elapsed==0,"timer reset");
 facility.facilityID="test";facility.isVerified=true;facility.isPlaceholder=false;
 q.facility=facility;q.isVerified=true;q.answers=new[]{"A","B"};q.correctIndex=0;
 Check(q.Eligible,"verified question accepted");q.isPlaceholder=true;Check(!q.Eligible,"placeholder question excluded");q.isPlaceholder=false;
 facility.isPlaceholder=true;Check(!q.Eligible,"placeholder facility excluded");facility.isPlaceholder=false;facility.isVerified=false;Check(!q.Eligible,"unverified facility excluded");facility.isVerified=true;
 q.correctIndex=3;Check(!q.Eligible,"invalid answer index excluded");q.correctIndex=0;q.facility=null;Check(!q.Eligible,"missing facility excluded");
 var objective=new MissionObjective{facility=facility};Check(objective.Key=="visit:test","facility ID reference");facility.fallbackName="New label";Check(objective.Key=="visit:test","renaming does not change target ID");
 Debug.Log("Mission logic tests passed: "+checks+" checks.");
 }finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(facility);UnityEngine.Object.DestroyImmediate(q);UnityEngine.Object.DestroyImmediate(rules);}
 }
}
}
#endif
