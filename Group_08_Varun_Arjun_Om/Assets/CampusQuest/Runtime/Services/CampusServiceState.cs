using System;
namespace CampusQuest {
[Serializable] public class CampusServiceState {
 public int book=-1,component=-1;
 public bool bookIssued,bookCollected,componentIssued,componentCollected,printing,printReady,printCollected,delivered;
 public bool ChooseBook(int i){if(i<0||bookIssued||bookCollected)return false;book=i;return true;}
 public bool IssueBook(){if(book<0||bookIssued)return false;bookIssued=true;return true;}
 public bool CollectBook(){if(!bookIssued||bookCollected)return false;bookCollected=true;return true;}
 public bool ChooseComponent(int i){if(i<0||componentIssued||componentCollected)return false;component=i;return true;}
 public bool IssueComponent(){if(component<0||componentIssued)return false;componentIssued=true;return true;}
 public bool CollectComponent(){if(!componentIssued||componentCollected)return false;componentCollected=true;return true;}
 public bool StartPrint(){if(printing||printReady||printCollected||delivered)return false;printing=true;return true;}
 public bool FinishPrint(){if(!printing)return false;printing=false;printReady=true;return true;}
 public bool CollectPrint(){if(!printReady||printCollected)return false;printCollected=true;return true;}
 public bool Deliver(){if(!printCollected||delivered)return false;delivered=true;printCollected=false;return true;}
 }
}
