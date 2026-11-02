#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
namespace CampusQuest.Editor {
public static class CampusSignCleanup {
 public static void Apply(GameObject root){
 var shader=Shader.Find("CampusQuest/DepthText");
 if(shader==null)throw new InvalidOperationException("Missing CampusDepthText.shader; copy the Shaders folder too.");
 // Use the font's own material so dynamic atlas rebuilds keep the texture correct.
 // Persist an override per label; runtime synchronizer updates the font atlas.
 const string path="Assets/CampusQuest/Generated/VisualUpgrade/Materials/DepthText.mat";
 var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
 if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}else mat.shader=shader;
 foreach(var tm in root.GetComponentsInChildren<TextMesh>(true)){
 string text=tm.text??"";
 if(text.Contains("SAMPLE POSTER")||text.Contains("Sample content")||text.Contains("SAMPLE STUDENT NOTICES")||text.Contains("Equipment: placeholder")){tm.gameObject.SetActive(false);continue;}
 if(!tm.gameObject.activeInHierarchy)continue;
 var renderer=tm.GetComponent<MeshRenderer>();if(renderer==null||tm.font==null)continue;
 mat.mainTexture=tm.font.material.mainTexture;renderer.sharedMaterial=mat;
 var sync=tm.GetComponent<CampusTextAtlasSync>();if(sync==null)sync=tm.gameObject.AddComponent<CampusTextAtlasSync>();
 float width=1.8f,height=.45f;
 var parent=tm.transform.parent;
 if(parent!=null&&parent.name.StartsWith("Mounted_room_name_")){width=1.27f;height=.46f;tm.text=text.Replace("\nPRACTICE","");}
 else if(parent!=null){var face=parent.Find("Board face");if(face!=null){width=face.localScale.x*.85f;height=face.localScale.y*.8f;}}
 if(tm.GetComponentInParent<LiftController>()!=null){width=Mathf.Min(width,.65f);height=Mathf.Min(height,.23f);}
 // Font metrics avoid depending on whether Unity has refreshed renderer bounds yet.
 tm.font.RequestCharactersInTexture(tm.text,tm.fontSize,tm.fontStyle);
 float max=0;foreach(var line in tm.text.Split('\n')){float advance=0;foreach(char ch in line){CharacterInfo info;if(tm.font.GetCharacterInfo(ch,out info,tm.fontSize,tm.fontStyle))advance+=info.advance;}max=Mathf.Max(max,advance);}
 float unit=tm.characterSize/10f;
 float w=max*unit,h=tm.text.Split('\n').Length*tm.fontSize*unit*1.2f;
 float scale=Mathf.Min(1f,Mathf.Min(width/Mathf.Max(w,.001f),height/Mathf.Max(h,.001f)));
 tm.characterSize*=scale;
 }
 EditorUtility.SetDirty(mat);
 }
}
}
#endif
