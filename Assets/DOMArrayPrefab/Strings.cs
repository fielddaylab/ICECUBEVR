using UnityEngine;
using System.Collections;
using System.IO;
using System;

public class Strings : MonoBehaviour
{
  public float offset = 0f;
  string icecubeFile = "Assets/DOMArrayPrefab/Icecube_Geometry_Data.txt";
  private const float BELOW_ICE = -1950.0f;

  private const int NUM_STRINGS = 86;
  private const int NUM_DOMS_PER_STRING = 64;

  public GameObject stringObject;
  public Transform cam;
  public float scaleMultiplier;

  void Start()
  {
    StreamReader reader = new StreamReader(icecubeFile);

    LineRenderer lineRen = null;
    Vector3[] pos = new Vector3[2];

    string line;

    while((line = reader.ReadLine()) != null)
    {
      string[] data = line.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

      string domIndex = data[1];
      string xVal = data[2];
      string yVal = data[3];
      string zVal = data[4];

      int domNum = Convert.ToInt32(domIndex) - 1;
      float xFloat = float.Parse(xVal);
      float yFloat = float.Parse(yVal);
      float zFloat = float.Parse(zVal);

      if(domNum <= 60)
      {
        Vector3 startPos = new Vector3(xFloat + offset, BELOW_ICE + zFloat, yFloat);

        if(domNum == 0)
        {
          startPos.y = -50.0f;
          pos[0] = startPos;
        }

        if(domNum == 59)
        {
          GameObject stringObj = (GameObject)Instantiate(stringObject);
          Vector3 domPos = new Vector3(xFloat + offset, BELOW_ICE + zFloat, yFloat);
          stringObj.transform.position = domPos;
          stringObj.transform.SetParent(transform);
          stringObj.layer = transform.gameObject.layer;

          pos[1] = new Vector3(xFloat + offset, BELOW_ICE + zFloat, yFloat);
          lineRen = stringObj.AddComponent<LineRenderer>();
          AnimationCurve curve = new AnimationCurve();
          float lw = 0.023f;
          curve.AddKey(0, lw);
          curve.AddKey(1, lw);
          lineRen.widthCurve = curve;
          lineRen.startColor = Color.black;
          lineRen.endColor = Color.black;
          lineRen.material = new Material(Shader.Find("Standard"));
          lineRen.material.color = Color.black;
          lineRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
          lineRen.receiveShadows = false;

          lineRen.SetPositions(pos);
        }
        pos[1] = pos[0];
      }
    }

    reader.Close();
  }

  void Update()
  {
    for(int i = 0; i < transform.childCount; ++i)
    {
      LineRenderer d = transform.GetChild(i).gameObject.GetComponent<LineRenderer>();
      if(d)
      {
        Vector3 vCam = cam.position;
        vCam.y = 0.0f;
        Vector3 lPos = d.transform.position;
        lPos.y = 0.0f;
        float w = Vector3.Distance(vCam, lPos);
        if(w > 20.0f) w *= scaleMultiplier;
        else          w = 0.023f;

        d.startWidth = w;
        d.endWidth = w;
      }
    }

  }
}

