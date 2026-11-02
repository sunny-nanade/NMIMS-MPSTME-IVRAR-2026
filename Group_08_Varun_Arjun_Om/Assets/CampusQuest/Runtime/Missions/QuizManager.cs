using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace CampusQuest {
public class QuizManager:MonoBehaviour {
 public QuizQuestionData[] finalQuestions=new QuizQuestionData[0];
 public readonly List<string> Review=new List<string>();
 QuizQuestionData[] batch;int index,attempts;bool final;
 public bool Active {get;private set;}
 public bool Answered {get;private set;}
 public string Feedback {get;private set;}
 public QuizQuestionData Current=>Active&&batch!=null&&index<batch.Length?batch[index]:null;
 public bool Begin(IEnumerable<QuizQuestionData> questions,bool finalRound){
 if(Active)return true;var m=MissionManager.Instance;
 batch=questions.Where(q=>q!=null&&q.Eligible&&m.Visited(q.facility)).Distinct().ToArray();
 if(batch.Length==0)return false;
 index=0;attempts=0;final=finalRound;Active=true;Answered=false;Feedback="";return true;
 }
 public void Submit(int choice){
 var q=Current;if(q==null||Answered||choice<0||choice>=q.answers.Length)return;
 attempts++;var m=MissionManager.Instance;
 if(choice==q.correctIndex){Answered=true;m.score.Award("quiz:"+q.questionID,m.score.rules.correctQuiz);Feedback="Correct. "+q.explanation;Review.Add(q.prompt+" | Correct: "+q.answers[q.correctIndex]+" | Attempts: "+attempts);}
 else {m.score.Penalty(m.score.rules.wrongQuiz);m.Mistake();Feedback=attempts>=2?"Hint: "+q.explanation:"Try again, or review the answer and continue.";}
 }
 public void ReviewAnswer(){var q=Current;if(q==null||Answered)return;Answered=true;Feedback=q.explanation;Review.Add(q.prompt+" | Reviewed answer: "+q.answers[q.correctIndex]+" | Needs more practice");}
 public void Next(){
 var q=Current;if(q==null||!Answered)return;MissionManager.Instance.Signal("quiz:"+q.questionID);
 index++;attempts=0;Answered=false;Feedback="";
 if(index>=batch.Length){Active=false;if(final)MissionManager.Instance.Signal("quiz:final");}
 }
 public void Cancel(){Active=false;Answered=false;Feedback="";batch=null;}
 public void ResetSession(){Cancel();Review.Clear();}
}
}
