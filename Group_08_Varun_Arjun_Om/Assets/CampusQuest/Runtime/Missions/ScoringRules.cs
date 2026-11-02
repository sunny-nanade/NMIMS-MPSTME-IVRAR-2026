using UnityEngine;
namespace CampusQuest {
[CreateAssetMenu(menuName="Campus Quest/Scoring Rules")]
public class ScoringRules:ScriptableObject {
 public int checkpoint=100,facility=50,correctQuiz=50,efficientRoute=25,wrongFloor=-10,wrongQuiz=-5;
}
}
