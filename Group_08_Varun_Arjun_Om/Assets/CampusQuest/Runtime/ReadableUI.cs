using UnityEngine;
namespace CampusQuest {
// Shared desktop styles. Call only during OnGUI; does not modify Unity's global skin.
public static class ReadableUI {
 static int builtSize=-1;
 static GUIStyle body,title,small,button,panel;
 static Texture2D normal,hover,pressed;
 public static int Size=>DesktopHUD.Instance==null?24:Mathf.Clamp(DesktopHUD.Instance.textSize,18,36);
 static Texture2D Solid(Color c){var t=new Texture2D(1,1){hideFlags=HideFlags.HideAndDontSave};t.SetPixel(0,0,c);t.Apply();return t;}
 static void Ensure(){
 if(normal==null){normal=Solid(new Color(.17f,.23f,.30f));hover=Solid(new Color(.25f,.37f,.49f));pressed=Solid(new Color(.12f,.35f,.48f));builtSize=-1;}
 if(builtSize==Size&&body!=null)return;builtSize=Size;
 body=new GUIStyle(GUI.skin.label){fontSize=Size,wordWrap=true,richText=false,padding=new RectOffset(0,0,4,8)};body.normal.textColor=Color.white;
 title=new GUIStyle(body){fontSize=Size+6,fontStyle=FontStyle.Bold};
 small=new GUIStyle(body){fontSize=Mathf.Max(20,Size-4)};
 button=new GUIStyle(GUI.skin.button){fontSize=Size,wordWrap=true,richText=false,padding=new RectOffset(16,16,12,12),margin=new RectOffset(0,0,5,5)};
 button.normal.background=normal;button.hover.background=hover;button.active.background=pressed;button.focused.background=hover;
 button.normal.textColor=button.hover.textColor=button.active.textColor=button.focused.textColor=Color.white;
 panel=new GUIStyle(){padding=new RectOffset(18,18,16,16)};
 }
 public static GUIStyle Body{get{Ensure();return body;}}
 public static GUIStyle Title{get{Ensure();return title;}}
 public static GUIStyle Small{get{Ensure();return small;}}
 public static GUIStyle ButtonStyle{get{Ensure();return button;}}
 public static GUIStyle Panel{get{Ensure();return panel;}}
 public static Rect Modal=>new Rect(Mathf.Max(12,(Screen.width-Mathf.Min(960,Screen.width-24))/2),12,Mathf.Min(960,Screen.width-24),Mathf.Max(100,Screen.height-24));
 public static bool Button(string text,float width){return GUILayout.Button(text,ButtonStyle,GUILayout.MinHeight(Mathf.Max(54,ButtonStyle.CalcHeight(new GUIContent(text),Mathf.Max(100,width-60)))));}
 public static void Backdrop(Rect r){var old=GUI.color;GUI.color=new Color(.035f,.055f,.08f,.98f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
 public static void Card(float x,float y,float width,string text,GUIStyle style){
 float h=style.CalcHeight(new GUIContent(text),width-28)+24;var r=new Rect(x,y,width,h);Backdrop(r);GUI.Label(new Rect(x+14,y+12,width-28,h-24),text,style);
 }
 public static void SizeControls(){
 if(DesktopHUD.Instance==null)return;
 GUILayout.BeginHorizontal();GUILayout.Label("Text size",Small);
 if(GUILayout.Button("A−",ButtonStyle,GUILayout.Width(80),GUILayout.MinHeight(48)))DesktopHUD.Instance.SetTextSize(Size-2);
 if(GUILayout.Button("A+",ButtonStyle,GUILayout.Width(80),GUILayout.MinHeight(48)))DesktopHUD.Instance.SetTextSize(Size+2);
 GUILayout.EndHorizontal();GUILayout.Space(8);
 }
}
}
