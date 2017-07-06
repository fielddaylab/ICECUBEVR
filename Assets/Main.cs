using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  public GameObject debug_cube_prefab;

  GameObject camera_house;
  GameObject main_camera;
  GameObject portal;
  GameObject portal_disk_prev;
  GameObject portal_disk_next;
  GameObject portal_border;
  GameObject portal_camera_next;
  GameObject portal_camera_prev;
  GameObject helmet;
  GameObject satellite;

  Vector3 default_portal_scale;
  Vector3 default_portal_position;
  Vector3 default_look_ahead;
  Vector3 look_ahead;
  Vector3 lazy_look_ahead;
  Vector3 default_satellite_position;
  Vector3 satellite_position;
  Vector3 satellite_velocity;

  public Material alpha_material;
  int alpha_id;
  float flash_alpha;

  GameObject[,] debug_cubes;

  int cur_layer_i;
  int next_layer_i;
  int prev_layer_i;
  int n_layers;
  int[] layers;
  int all_layer;

  bool mouse_captured;
  float mouse_x;
  float mouse_y;

  int in_portal_motion;
  int out_portal_motion;
  int max_portal_motion;

  Vector2 getEuler(Vector3 v)
  {
    float plane_dist = new Vector2(v.x,v.z).magnitude;
    return new Vector2(Mathf.Atan2(v.y,plane_dist),-1*(Mathf.Atan2(v.z,v.x)-Mathf.PI/2));
  }
  Quaternion rotationFromEuler(Vector2 euler)
  {
    return Quaternion.Euler(-Mathf.Rad2Deg*euler.x, Mathf.Rad2Deg*euler.y, 0);
  }

  void Start()
  {
    camera_house       = GameObject.Find("CameraHouse");
    main_camera        = GameObject.Find("Main Camera");
    portal             = GameObject.Find("Portal");
    portal_disk_next   = GameObject.Find("Disk_Next");
    portal_disk_prev   = GameObject.Find("Disk_Prev");
    portal_border      = GameObject.Find("Border");
    portal_camera_next = GameObject.Find("Portal_Camera_Next");
    portal_camera_prev = GameObject.Find("Portal_Camera_Prev");
    helmet             = GameObject.Find("Helmet");
    satellite          = GameObject.Find("Satellite");

    alpha_id = Shader.PropertyToID("alpha");
    flash_alpha = 0;

    default_portal_scale = portal.transform.localScale;
    default_portal_position = portal.transform.position;

    default_satellite_position = new Vector3(1.5f,1.5f,10);
    satellite_position = default_satellite_position;
    satellite_velocity = new Vector3(0,0,-0.01f);
    satellite.transform.position = satellite_position;

    default_look_ahead = new Vector3(0,0,1);
    look_ahead = default_look_ahead;
    lazy_look_ahead = default_look_ahead;

    n_layers = 5;
    layers = new int[n_layers];
    for(int i = 0; i < n_layers; i++)
      layers[i] = LayerMask.NameToLayer("Set_"+i);
    all_layer = LayerMask.NameToLayer("Set_ALL");
    cur_layer_i = 0;
    prev_layer_i = 0;
    next_layer_i = 1;

    debug_cubes = new GameObject[n_layers,3*3*3];
    for(int l = 0; l < n_layers; l++)
    for(int x = -1; x <= 1; x++)
    for(int y = -1; y <= 1; y++)
    for(int z = -1; z <= 1; z++)
    {
      int i = ((x+1)*9+(y+1)*3+(z+1)*1);
      debug_cubes[l,i] = (GameObject)Instantiate(debug_cube_prefab);
      debug_cubes[l,i].transform.position = new Vector3(x*10,y*10,z*10);
      debug_cubes[l,i].layer = layers[l];
      switch(l)
      {
        case 0: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.white); break;
        case 1: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.red); break;
        case 2: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.yellow); break;
        case 3: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.green); break;
        case 4: debug_cubes[l,i].GetComponent<Renderer>().material.SetColor("_Color", Color.blue); break;
      }
    }

    mouse_captured = false;
    mouse_x = 0;
    mouse_y = 0;

    in_portal_motion = 0;
    out_portal_motion = 0;
    max_portal_motion = 100;
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

    if(Input.GetKeyDown("space"))
    {
      if(in_portal_motion == 0 && out_portal_motion == 0) in_portal_motion = 1;
    }

    if(in_portal_motion > 0) in_portal_motion++;
    if(in_portal_motion > max_portal_motion)
    {
      in_portal_motion = 0;
      out_portal_motion = 1;
      prev_layer_i = cur_layer_i;
      cur_layer_i = next_layer_i;
      next_layer_i = (next_layer_i+1)%n_layers;

      main_camera.GetComponent<Camera>().cullingMask = (1 << layers[cur_layer_i]) | (1 << all_layer);
      portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[next_layer_i]) | (1 << all_layer);
      portal_camera_prev.GetComponent<Camera>().cullingMask = (1 << layers[prev_layer_i]) | (1 << all_layer);
      portal.layer = layers[cur_layer_i];
      portal_disk_next.layer = layers[cur_layer_i];
      portal_disk_prev.layer = layers[cur_layer_i];
      portal_border.layer = layers[cur_layer_i];
    }
    if(out_portal_motion > 0) out_portal_motion++;
    if(out_portal_motion > max_portal_motion)
    {
      out_portal_motion = 0;
    }

    if(in_portal_motion > 0)
    {
      float t = in_portal_motion/(float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x,default_portal_position.y,Mathf.Lerp(default_portal_position.z,0,t));
      float engulf = t-1;
      engulf *= -engulf;
      engulf += 1;
      portal.transform.localScale = new Vector3(default_portal_scale.x*engulf,default_portal_scale.y*engulf,default_portal_scale.z*engulf);
    }
    else if(out_portal_motion > 0)
    {
      float t = out_portal_motion/(float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x,default_portal_position.y,Mathf.Lerp(0,-default_portal_position.z,t));
      float engulf = t;
      engulf *= -engulf;
      engulf += 1;
      portal.transform.localScale = new Vector3(default_portal_scale.x*engulf,default_portal_scale.y*engulf,default_portal_scale.z*engulf);
    }
    else
    {
      portal.transform.localPosition = default_portal_position;
      portal.transform.localScale = new Vector3(0,0,0);
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

    look_ahead = main_camera.transform.rotation*default_look_ahead;
    lazy_look_ahead = Vector3.Lerp(lazy_look_ahead,look_ahead,0.1f);
    helmet.transform.position = main_camera.transform.position;
    helmet.transform.rotation = rotationFromEuler(getEuler(lazy_look_ahead));

    satellite_position += satellite_velocity;
    satellite.transform.position = satellite_position;
    if(cur_layer_i != 1) satellite.transform.position = default_satellite_position;

    if(in_portal_motion > 0)
      flash_alpha = (float)in_portal_motion/max_portal_motion;
    else if(out_portal_motion > 0)
      flash_alpha = 1.0f-((float)out_portal_motion/max_portal_motion);
    else
      flash_alpha = 0;
    flash_alpha *= 1.1f;
    if(flash_alpha > 1) flash_alpha = 1;
    flash_alpha = flash_alpha*flash_alpha;
    flash_alpha = flash_alpha*flash_alpha;
    alpha_material.SetFloat(alpha_id,flash_alpha);
  }

}

