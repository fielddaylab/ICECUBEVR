using UnityEngine;
using System.Collections;
using System.IO;
using System;

public class DomArrayGenerator : MonoBehaviour {

  public GameObject domObject;
  public string geometryFile = "Icecube_Geometry_Data.txt";

  private const int NUM_STRINGS = 86;
  private const int NUM_DOMS_PER_STRING = 64;

  public GameObject[,] DOMArray = new GameObject[NUM_STRINGS,NUM_DOMS_PER_STRING];

  void Start()
  {
    StreamReader reader = new StreamReader(geometryFile);

    Vector3[] pos = new Vector3[2];

    string line;
    while((line = reader.ReadLine()) != null)
    {
      string[] data = line.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

      int domUnitNum = Convert.ToInt32(data[0])-1;
      int domNum     = Convert.ToInt32(data[1])-1;
      Vector3 domPos = new Vector3(float.Parse(data[2]),float.Parse(data[4]),float.Parse(data[3])); //x,z,y

      GameObject tableDom = (GameObject)Instantiate(domObject);
      tableDom.transform.SetParent(transform);

      tableDom.transform.position = domPos;
      tableDom.transform.localScale.Set(10.0f, 10.0f, 10.0f);
      tableDom.GetComponent<DOMController>().stringNum = domUnitNum;
      tableDom.GetComponent<DOMController>().domNum = domNum;

      if(domNum <= 60)
      {
        if(domNum == 0) pos[0] = domPos;
        pos[1] = pos[0];
      }

      DOMArray[domUnitNum,domNum] = tableDom;
    }

    reader.Close();
  }

  private void SetLayersRecursively(GameObject obj, int layer)
  {
    if(!obj) return;
    obj.layer = layer;
    foreach(Transform child in obj.transform)
    {
      if(!child) continue;
      SetLayersRecursively(child.gameObject, layer);
    }
  }
}

