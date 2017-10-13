using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public class Maintenance : MonoBehaviour
{
  enum SPEC { VIZ, GAM, NEU, COUNT };
  enum SCENE { ICE, VOYAGER, NOTHING, EXTREME, EARTH, CREDITS, COUNT };
  string[] spec_names;
  string[] scene_names;
  string[,] layer_names;
  GameObject[,] sets;

  GameObject[] icecube;
  GameObject[] voyager;
  GameObject[] milky;
  GameObject[] blackhole;
  GameObject[] earth;

  private void setAllLayers(GameObject parent, int layer)
  {
    foreach(Transform child_transform in parent.transform)
    {
      GameObject child = child_transform.gameObject;
      child.layer = layer;
      setAllLayers(child,layer);
    }
  }

  private void setAllLights(GameObject parent)
  {
    foreach(Transform child_transform in parent.transform)
    {
      GameObject child = child_transform.gameObject;
      Light light = child.GetComponent<Light>();
      if(light && light.cullingMask != 0)
        light.cullingMask = 1 << child.layer;
      setAllLights(child);
    }
  }

  public void performMaintenance()
  {
    spec_names = new string[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SPEC.VIZ: name = "Viz"; break;
        case (int)SPEC.GAM: name = "Gam"; break;
        case (int)SPEC.NEU: name = "Neu"; break;
      }
      spec_names[i] = name;
    }

    scene_names = new string[(int)SCENE.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SCENE.ICE:     name = "Ice"; break;
        case (int)SCENE.VOYAGER: name = "Voyager"; break;
        case (int)SCENE.NOTHING: name = "Nothing"; break;
        case (int)SCENE.EXTREME: name = "Extreme"; break;
        case (int)SCENE.EARTH:   name = "Earth"; break;
        case (int)SCENE.CREDITS: name = "Credits"; break;
      }
      scene_names[i] = name;
    }

    layer_names = new string[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      for(int j = 0; j < (int)SCENE.COUNT; j++)
      {
        layer_names[i,j] = "Set_"+scene_names[j]+"_"+spec_names[i];
      }
    }

    sets = new GameObject[(int)SPEC.COUNT,(int)SCENE.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      for(int j = 0; j < (int)SCENE.COUNT; j++)
      {
        sets[i,j] = GameObject.Find(layer_names[i,j]);
      }
    }

    for(int j = 0; j < (int)SCENE.COUNT; j++)
    {
      GameObject master_set = sets[0,j];
      foreach(Transform master_child in master_set.transform)
      {
        Match m = Regex.Match(master_child.name, "_"+spec_names[0]+"$");
        if(m.Success)
        {
          Regex reg = new Regex("_"+spec_names[0]+"$");
          string master_name = reg.Replace(master_child.name,"");
          for(int i = 1; i < (int)SPEC.COUNT; i++)
          {
            GameObject servant_set = sets[i,j];
            foreach(Transform servant_child in servant_set.transform)
            {
              if(servant_child.name.Equals(master_name+"_"+spec_names[i]))
              {
                servant_child.position   = master_child.position;
                servant_child.rotation   = master_child.rotation;
                servant_child.localScale = master_child.localScale;
              }
            }
          }
        }
      }
    }

    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      for(int j = 0; j < (int)SCENE.COUNT; j++)
      {
        setAllLayers(sets[i,j],sets[i,j].layer);
      }
    }

    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      for(int j = 0; j < (int)SCENE.COUNT; j++)
      {
        setAllLights(sets[i,j]);
      }
    }

  }

}

