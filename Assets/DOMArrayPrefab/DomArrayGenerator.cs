using UnityEngine;
using System.Collections;
using System.IO;
using System;

public class DomArrayGenerator : MonoBehaviour {

  public GameObject domObject;
  public string geometryFile = "Icecube_Geometry_Data.txt";
  private const float BELOW_ICE = -1950.0f;

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

      string stringIndex = data [0];
      string domIndex = data [1];
      string xVal = data [2];
      string yVal = data [3];
      string zVal = data [4];

      int domUnitNum = Convert.ToInt32(stringIndex)-1;
      int domNum = Convert.ToInt32(domIndex)-1;
      float xFloat = float.Parse(xVal);
      float yFloat = float.Parse (yVal);
      float zFloat = float.Parse (zVal);

      GameObject tableDom = (GameObject)Instantiate(domObject);

      Vector3 domPos = new Vector3(xFloat, BELOW_ICE + zFloat, yFloat);

      tableDom.transform.position = domPos;
      tableDom.transform.SetParent(transform);
      tableDom.transform.localScale.Set(10.0f, 10.0f, 10.0f);
      tableDom.GetComponent<DOMController>().stringNum = domUnitNum;
      tableDom.GetComponent<DOMController>().domNum = domNum;
      tableDom.layer = LayerMask.NameToLayer("Set_DOM");

      if(domNum <= 60)
      {
        if(domNum == 0) pos[0] = new Vector3(xFloat, BELOW_ICE + zFloat, yFloat);
        pos[1] = pos[0];
      }

      DOMArray[domUnitNum, domNum] = tableDom;
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

