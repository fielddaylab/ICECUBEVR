using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  public GameObject debug_cube_prefab;

  GameObject camera_house;
  new GameObject camera;

  GameObject[] debug_cubes;

  bool mouse_captured;
  float mouse_x;
  float mouse_y;

  void Start()
  {
    camera_house  = GameObject.Find("CameraHouse");
    camera        = GameObject.Find("Main Camera");

    debug_cubes = new GameObject[3*3*3];
    for(int x = -1; x <= 1; x++)
    for(int y = -1; y <= 1; y++)
    for(int z = -1; z <= 1; z++)
    {
      int i = ((x+1)*9+(y+1)*3+(z+1)*1);
      debug_cubes[i] = (GameObject)Instantiate(debug_cube_prefab);
      debug_cubes[i].transform.position = new Vector3(x*10,y*10,z*10);
    }

    mouse_captured = false;
    mouse_x = 0;
    mouse_y = 0;
  }

  void Update()
  {
    if(Input.GetMouseButtonDown(0))
    {
      mouse_captured = !mouse_captured;
      if(mouse_captured)
      {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
      }
      else
      {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
      }
    }

    if(mouse_captured)
    {
      mouse_x += Input.GetAxis("Mouse X")*10;
      mouse_y += Input.GetAxis("Mouse Y")*10;
      camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);
    }
    else
    {
      mouse_x = 0;
      mouse_y = 0;
    }
  }

}

