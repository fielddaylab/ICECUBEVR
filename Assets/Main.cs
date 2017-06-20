using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  GameObject camera_house;
  new GameObject camera;

  void Start()
  {
    camera_house  = GameObject.Find("CameraHouse");
    camera        = GameObject.Find("Main Camera");
  }

  void Update()
  {

  }

}

