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
  GameObject cam_reticle;
  GameObject cam_spinner;
  GameObject gaze;
  GameObject gaze_reticle;
  GameObject gaze_spinner;

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

  public AudioClip sound;
  AudioSource source;

  public GameObject star_prefab;

  GameObject[,] debug_cubes;

  int cur_layer_i;
  int next_layer_i;
  int prev_layer_i;
  int n_layers;
  int[] layers;
  int all_layer;
  int cam_layer;
  int portal_layer;

  bool mouse_captured;
  bool mouse_just_captured;
  float mouse_x;
  float mouse_y;

  int in_portal_motion;
  int out_portal_motion;
  int max_portal_motion;

  const int SPIN_STATE_IDLE = 0;
  const int SPIN_STATE_START = 1;
  const int SPIN_STATE_RUN = 2;
  const int SPIN_STATE_STOPPING = 3;
  const int SPIN_STATE_STOP = 4;
  int spin_state;
  int spin;
  int spin_max;
  int spins_per;

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
    cam_reticle        = GameObject.Find("Cam_Reticle");
    cam_spinner        = GameObject.Find("Cam_Spinner");
    gaze               = GameObject.Find("Gaze");
    gaze_reticle       = GameObject.Find("Gaze_Reticle");
    gaze_spinner       = GameObject.Find("Gaze_Spinner");

    alpha_id = Shader.PropertyToID("alpha");
    flash_alpha = 0;
    source = GetComponent<AudioSource>();

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
    cam_layer = LayerMask.NameToLayer("Set_Cam_Only");
    portal_layer = LayerMask.NameToLayer("Set_Portal_Only");
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
    mouse_just_captured = true;
    mouse_x = Screen.width/2;
    mouse_y = Screen.height/2;

    camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);

    in_portal_motion = 0;
    out_portal_motion = 0;
    max_portal_motion = 100;

    //SPIN_STATE_IDLE     = 0;
    //SPIN_STATE_START    = 1;
    //SPIN_STATE_RUN      = 2;
    //SPIN_STATE_STOPPING = 3;
    //SPIN_STATE_STOP     = 4;

    spin_state = SPIN_STATE_IDLE;
    spin = 0;
    spin_max = 200;
    spins_per = 4;

    GameObject[] star_groups;
    GameObject star;
    Vector3[] star_positions;
    Vector3 starpos;

    int n_stars = 5000;
    int n_groups = (int)Mathf.Ceil(n_stars/1000);
    int n_stars_in_group;
    star_groups = new GameObject[n_groups];
    star = (GameObject)Instantiate(star_prefab);

    star_positions = new Vector3[n_stars];
    for(int i = 0; i < n_stars; i++)
    {
      bool good_star = false;
      starpos = new Vector3(Random.Range(-1f,1f),Random.Range(-1f,1f),Random.Range(-1f,1f));
      good_star = (starpos.sqrMagnitude < Random.Range(0f,1f));
      while(!good_star)
      {
        starpos = new Vector3(Random.Range(-1f,1f),Random.Range(-1f,1f),Random.Range(-1f,1f));
        good_star = (starpos.sqrMagnitude < Random.Range(0f,1f));
      }
      starpos = starpos.normalized;
      star_positions[i] = starpos;
    }

    for(int i = 0; i < n_groups; i++)
    {
      n_stars_in_group = Mathf.Min(1000,n_stars);
      CombineInstance[] combine = new CombineInstance[n_stars_in_group];

      for(int j = 0; j < n_stars_in_group; j++)
      {
        starpos = star_positions[i*n_stars_in_group+j];
        float r = Random.Range(0f,1f);
        starpos *= Mathf.Lerp(20f,30f,r*r);

        star.transform.position = starpos;
        star.transform.rotation = Quaternion.Euler(Random.Range(0f,360f),Random.Range(0f,360f),Random.Range(0f,360f));
        star.transform.localScale = new Vector3(0.05f,0.05f,0.05f);

        combine[j].mesh = star.transform.GetComponent<MeshFilter>().mesh;
        combine[j].transform = star.transform.localToWorldMatrix;
      }

      star_groups[i] = (GameObject)Instantiate(star_prefab);
      star_groups[i].transform.position = new Vector3(0,0,0);
      star_groups[i].transform.rotation = Quaternion.Euler(0,0,0);
      star_groups[i].transform.localScale = new Vector3(1,1,1);
      star_groups[i].transform.GetComponent<MeshFilter>().mesh = new Mesh();
      star_groups[i].transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);

      n_stars -= n_stars_in_group;
    }
    Destroy(star,0f);
  }

  void Update()
  {
    if(Input.GetMouseButtonDown(0))
    {
      mouse_captured = !mouse_captured;
      if(mouse_captured)
      {
        mouse_just_captured = true;
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
    }

    if(in_portal_motion > 0) in_portal_motion++;
    if(in_portal_motion > max_portal_motion)
    {
      in_portal_motion = 0;
      out_portal_motion = 1;
      prev_layer_i = cur_layer_i;
      cur_layer_i = next_layer_i;
      next_layer_i = (next_layer_i+1)%n_layers;

      main_camera.GetComponent<Camera>().cullingMask = (1 << layers[cur_layer_i]) | (1 << cam_layer) | (1 << all_layer);
      portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[next_layer_i]) | (1 << portal_layer) | (1 << all_layer);
      portal_camera_prev.GetComponent<Camera>().cullingMask = (1 << layers[prev_layer_i]) | (1 << portal_layer) | (1 << all_layer);
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
      float in_x = Input.GetAxis("Mouse X")*10;
      float in_y = Input.GetAxis("Mouse Y")*10;
      if(!mouse_just_captured)
      {
        mouse_x += in_x;
        mouse_y += in_y;
        camera_house.transform.rotation = Quaternion.Euler((mouse_y-Screen.height/2)*-2, (mouse_x-Screen.width/2)*2, 0);
      }
      else
      {
        if(in_x != 0 || in_y != 0) mouse_just_captured = false;
      }
    }

    look_ahead = main_camera.transform.rotation*default_look_ahead;
    lazy_look_ahead = Vector3.Lerp(lazy_look_ahead,look_ahead,0.1f);
    helmet.transform.position = main_camera.transform.position;
    helmet.transform.rotation = rotationFromEuler(getEuler(lazy_look_ahead));

    satellite_position += satellite_velocity;
    if(cur_layer_i != 1) satellite_position = default_satellite_position;
    satellite.transform.position = satellite_position;

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

    if(spin_state > SPIN_STATE_IDLE)
    {
      spin++;
      if(spin > spin_max)
      {
        spin = 0;
        if(spin_state != SPIN_STATE_RUN) spin_state = (spin_state+1)%(SPIN_STATE_STOP+1);
      }
    }

    switch(spin_state)
    {
      case SPIN_STATE_IDLE: break;
      case SPIN_STATE_START:
      case SPIN_STATE_STOP:
        spin++;
        if(spin > spin_max)
        {
          spin = 0;
          spin_state = (spin_state+1)%(SPIN_STATE_STOP+1);
        }
        break;
      case SPIN_STATE_RUN:
      case SPIN_STATE_STOPPING:
        if(spin > spin_max/spins_per) spin = spin%(spin_max/spins_per);
        spin++;
        if(spin > spin_max/spins_per)
        {
          spin = 0;
          spin_state++;
        }
        break;
    }

    float shrink;
    float rot;

    shrink = 0.0f;
    rot = 0.0f;
         if(spin_state == SPIN_STATE_START)    shrink = (float)spin/spin_max;
    else if(spin_state == SPIN_STATE_RUN)      shrink = 1.0f;
    else if(spin_state == SPIN_STATE_STOPPING) shrink = 1.0f;
    else if(spin_state == SPIN_STATE_STOP)     shrink = 1.0f-((float)spin/spin_max);
    rot = ((float)spin/spin_max)*360.0f*spins_per;

    gaze_spinner.transform.localScale = new Vector3(shrink,shrink,shrink);
    gaze_spinner.transform.localRotation = Quaternion.Euler(0,0,rot);
    cam_spinner.transform.localScale = new Vector3(shrink,shrink,shrink);
    cam_spinner.transform.localRotation = Quaternion.Euler(0,0,rot);

    float distance = Vector3.Distance(gaze_reticle.transform.position,cam_reticle.transform.position);
    if(spin_state == SPIN_STATE_IDLE &&  distance < 0.2)
    {
      spin_state = SPIN_STATE_START;
    }
    if(spin_state == SPIN_STATE_START && distance > 0.2)
    {
      spin_state = SPIN_STATE_STOP;
    }
    if(spin_state == SPIN_STATE_RUN)
    {
      spin_state = SPIN_STATE_STOPPING;
      if(in_portal_motion == 0 && out_portal_motion == 0) in_portal_motion = 1;
      source.PlayOneShot(sound,1);
    }
  }

}

