using UnityEngine;
namespace CampusQuest {
[CreateAssetMenu(menuName="Campus Quest/Quiz Question")]
public class QuizQuestionData:ScriptableObject {
 public string questionID;
 public FacilityData facility;
 [TextArea]public string prompt,explanation;
 public string[] answers=new string[0];
 public int correctIndex;
 public bool isVerified, isPlaceholder;
 public bool Eligible=>isVerified&&!isPlaceholder&&facility!=null&&facility.Verified&&answers!=null&&answers.Length>=2&&correctIndex>=0&&correctIndex<answers.Length;
}
}
