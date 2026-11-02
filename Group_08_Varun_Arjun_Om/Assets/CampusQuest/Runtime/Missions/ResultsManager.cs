using System.Linq;
using UnityEngine;
namespace CampusQuest {
public class ResultsManager:MonoBehaviour {
 public bool Visible {get;private set;}
 public bool Completed {get;private set;}
 public string Report {get;private set;}
 public void Show(bool complete,string reason){
 var m=MissionManager.Instance;Visible=true;Completed=complete;
 string title=complete&&!m.DebugUsed?"Campus Ready — prototype badge":m.DebugUsed?"Practice run — debug tools used":"Orientation session ended";
 Report=title+"\n"+reason+"\nScore: "+m.score.Total+" | Missions: "+m.CompletedCount+" / "+m.missions.Length+"\nActive time: "+System.TimeSpan.FromSeconds(m.timer.Elapsed).ToString(@"mm\:ss")+
 "\n\nFACILITIES FOUND\n"+string.Join("\n",m.facilities.Where(m.Visited).Select(f=>f.DisplayName+(f.Verified?"":" [provisional]")))+
 "\n\nEXPLORE NEXT\n"+string.Join("\n",m.facilities.Where(f=>!m.Visited(f)).Select(f=>f.DisplayName))+
 "\n\nANSWER REVIEW\n"+string.Join("\n\n",m.quiz.Review)+
 "\n\nClassroom/AR-VR placement remains provisional. This prototype does not establish improved real-world engagement.";
 }
 public void ResetSession(){Visible=false;Completed=false;Report="";}
}
}
