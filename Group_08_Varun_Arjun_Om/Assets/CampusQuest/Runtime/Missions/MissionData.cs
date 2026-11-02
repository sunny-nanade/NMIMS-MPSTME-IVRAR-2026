using System;
using UnityEngine;
namespace CampusQuest {
[Serializable] public class MissionObjective {
 public string eventID;
 public FacilityData facility;
 public string instruction;
 public string Key=>facility==null?eventID:"visit:"+facility.facilityID;
}
[CreateAssetMenu(menuName="Campus Quest/Mission")]
public class MissionData:ScriptableObject {
 public string missionID,title;
 [TextArea]public string briefing,hint;
 public int budgetSeconds=120;
 public int[] allowedFloors=new int[0];
 public MissionObjective[] objectives=new MissionObjective[0];
 public QuizQuestionData question;
 public bool finalQuiz;
}
}
