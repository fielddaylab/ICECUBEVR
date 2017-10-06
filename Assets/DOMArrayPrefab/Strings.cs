﻿using UnityEngine;
using System.Collections;
using System.IO;
using System;

public class Strings : MonoBehaviour
{
  public float offset = 0f;
  string icecubeFile = "Assets/DOMArrayPrefab/Icecube_Geometry_Data.txt";
  private const float BELOW_ICE = -1950.0f;
  private bool first = true;

  //should keep track of all created objects here in a sort of "dom" matrix..
  //this could allow for very efficient DOM lookups later...
  private const int NUM_STRINGS = 86;
  private const int NUM_DOMS_PER_STRING = 64;

  //line updating variables...
  public GameObject stringObject;
  public Transform cam;
  public float scaleMultiplier;

  void Start()
  {
    StreamReader reader = new StreamReader(icecubeFile);

    LineRenderer lineRen = null;
    Vector3[] pos = new Vector3[2];

    string line;

    while ((line = reader.ReadLine()) != null)
    {

      string[] data = line.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries);

      //grab each column of data
      string domIndex = data[1];
      string xVal = data[2];
      string yVal = data[3];
      string zVal = data[4];

      //parsing each piece of data
      int domNum = Convert.ToInt32(domIndex) - 1;
      float xFloat = float.Parse(xVal);
      float yFloat = float.Parse(yVal);
      float zFloat = float.Parse(zVal);


      if (domNum <= 60)
      {

        Vector3 startPos = new Vector3(xFloat + offset, BELOW_ICE + zFloat, yFloat);

        //avgX += xFloat;
        //avgZ += yFloat;
        if (domNum == 0)
        {
          startPos.y = -50.0f;
          pos[0] = startPos;
        }

        if (first == false)
        {
          /*lineRen = dom.AddComponent<LineRenderer> ();
            lineRen.SetWidth (.023f, .023f);
            lineRen.SetColors (Color.black, Color.black);
            lineRen.material = new Material (Shader.Find ("Standard"));//Particles/Additive"));
            lineRen.material.color = Color.black;
            lineRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRen.receiveShadows = false;

            Vector3[] pos2 = new Vector3[2];
            pos2 [0] = pos [0];
            pos2 [1] = pos [1];

            pos2 [0].y += 0.64f;
            pos2 [1].y -= 0.62f;
            lineRen.SetPositions (pos2);*/
        }
        else
        {
          /* lineRen = dom.AddComponent<LineRenderer>();
             lineRen.SetWidth(.023f, .023f);
             lineRen.SetColors(Color.black, Color.black);
             lineRen.material = new Material(Shader.Find("Standard"));//Particles/Additive"));
             lineRen.material.color = Color.black;
             lineRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
             lineRen.receiveShadows = false;

             Vector3[] pos2 = new Vector3[2];
             pos2[0] = new Vector3(xFloat, 0.0f, yFloat);
             pos2[1] = pos[0];

             pos2[1].y += 0.64f;
          //pos2[1].y -= 0.62f;
          lineRen.SetPositions(pos2);*/
        }

        if (domNum == 59)
        {
          GameObject stringObj = (GameObject)Instantiate(stringObject); //GameObject.CreatePrimitive(PrimitiveType.Sphere);
          Vector3 domPos = new Vector3(xFloat + offset, BELOW_ICE + zFloat, yFloat);
          stringObj.transform.position = domPos;
          stringObj.transform.SetParent(transform);
          stringObj.layer = transform.gameObject.layer;

          //how to make line widths different for oculus vs. regular user?
          pos[1] = new Vector3(xFloat + offset, BELOW_ICE + zFloat, yFloat);
          lineRen = stringObj.AddComponent<LineRenderer>();
          AnimationCurve curve = new AnimationCurve();
          float lw = 0.023f;
          curve.AddKey(0, lw);
          curve.AddKey(1, lw);
          lineRen.widthCurve = curve;
          lineRen.startColor = Color.black;
          lineRen.endColor = Color.black;
          lineRen.material = new Material(Shader.Find("Standard"));//Particles/Additive"));
          lineRen.material.color = Color.black;
          lineRen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
          lineRen.receiveShadows = false;

          //Vector3[] pos2 = new Vector3[2];
          //pos2[0] = new Vector3(xFloat, 0.0f, yFloat);
          //pos2[1] = pos[0];

          //pos2[1].y += 0.64f;
          //pos2[1].y -= 0.62f;
          lineRen.SetPositions(pos);
        }
        pos[1] = pos[0];
        first = false;
      }
      else
      {
        first = true;
      }

    }   //end while()

    reader.Close();
  }//end Start()

  void Update()
  {
    for (int i = 0; i < transform.childCount; ++i)
    {
      LineRenderer d = transform.GetChild(i).gameObject.GetComponent<LineRenderer>();
      if(d != null)
      {
        Vector3 vCam = cam.position;
        vCam.y = 0.0f;
        Vector3 lPos = d.transform.position;
        lPos.y = 0.0f;
        float w = Vector3.Distance(vCam, lPos);
        if (w > 20.0f)
        {
          w *= scaleMultiplier;
        }
        else
        {
          w = 0.023f;
        }

        d.startWidth = w;
        d.endWidth = w;
      }
    }

  }//end Update()
}//end Class
