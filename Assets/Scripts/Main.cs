﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
  GameObject camera_house;
  GameObject main_camera;
  Skybox main_camera_skybox;
  GameObject portal_projection;
  GameObject portal;
  GameObject portal_camera_next;
  Skybox portal_camera_next_skybox;
  GameObject helmet;
  GameObject helmet_light;
  Light helmet_light_light;
  GameObject cam_reticle;
  GameObject cam_spinner;
  GameObject gaze_projection;
  GameObject gaze_reticle;
  GameObject spec_projection;
  GameObject spec_viz_reticle;
  GameObject spec_gam_reticle;
  GameObject spec_neu_reticle;
  GameObject spec_sel_reticle;
  GameObject eyeray;
  GameObject ar_group;
  GameObject ar_camera_project;
  GameObject ar_camera_static;
  GameObject ar_alert;
  GameObject ar_timer;
  TextMesh ar_timer_text;

  int MAX_LABELS = 5;
  GameObject[] ar_labels;
  GameObject[] ar_label_offsets;
  TextMesh[] ar_label_texts;
  LineRenderer[] ar_label_lines;
  GameObject[] ar_progresses;
  GameObject[] ar_progress_offsets;
  LineRenderer[] ar_progress_lines;

  GameObject[] icecube;
  GameObject[] voyager;
  GameObject[] pluto;
  GameObject[] vearth;
  GameObject[] milky;
  GameObject[] nearth;
  GameObject[] blackhole;
  GameObject[] esun;
  GameObject[] earth;
  //GameObject stars;
  //GameObject starsscale;

  Vector3 default_portal_scale;
  Vector3 default_portal_position;
  Vector3 default_look_ahead;
  Vector3 look_ahead;
  Vector3 lazy_look_ahead;
  Vector3 very_lazy_look_ahead;
  Vector3 player_head;
  Vector3 default_satellite_position;
  Vector3 satellite_position;
  Vector3 satellite_velocity;

  public Material alpha_material;
  public GameObject star_prefab;
  public GameObject ar_label_prefab;
  public GameObject ar_progress_prefab;

  public Color scene0_helmet_color;
  public Color scene1_helmet_color;
  public Color scene2_helmet_color;
  public Color scene3_helmet_color;
  public Color scene4_helmet_color;
  Color[] helmet_colors; //wrangle into array in Start for easier accessibility

  public float extreme_camera_shake = 0.2f;
  public float extreme_helmet_shake = 0.001f;

  public float label_offset_local_scale = 8f;
  public float label_local_scale = 5f;
  public Vector3 label_local_position = new Vector3(10,10,0f);
  public Vector3 line_0 = new Vector3(-8,0,0);
  public Vector3 line_1 = new Vector3(-12,0,0);
  public Vector3 line_2 = new Vector3(-14,-4,0);

  int alpha_id;
  float flash_alpha;
  int time_mod_twelve_pi_id;
  float time_mod_twelve_pi;
  int jitter_id;
  float jitter;
  float jitter_countdown;
  int jitter_state;
  float jitter_min_downtime = 0.1f;
  float jitter_max_downtime = 1f;
  float jitter_min_uptime = 0.1f;
  float jitter_max_uptime = 0.4f;

  float alert_t;
  float timer_t;

  AudioSource voiceover_audiosource;
  bool voiceover_was_playing;
  //AudioSource sfx_audiosource;
  string[,] voiceover_files;
  AudioClip[,] voiceovers;
  bool[,] voiceovers_played;

  string[] skybox_file_names = new string[]
  {
    "Classic Skybox/sky12", //ice
    "GalaxyBox2/Skybox2_1/Skybox2_1", //voyager
    "GalaxyBox2/Skybox2_4/Skybox2_4", //nothing
    "GalaxyBox2/Skybox2_1/Skybox2_1", //extreme
    "GalaxyBox2/Skybox2_1/Skybox2_1", //earth
  };
  string[,] skybox_files;
  Material[,] skyboxes;

  Vector3[] scene_centers = new Vector3[]
  {
    new Vector3(0f, 0f, 0f), //ice
    new Vector3(0f, 0f, 0f), //voyager
    new Vector3(0f, 0f, 0f), //nothing
    new Vector3(0f, 0f, 0f), //extreme
    new Vector3(0f, 0f, 0f), //earth
  };
  float[] scene_rots = new float[]
  {
    0f, //ice
    0f, //voyager
    0f, //nothing
    0f, //extreme
    0f, //earth
  };
  float[] scene_rot_deltas = new float[]
  {
    0.0f, //ice
    0.0f, //voyager
    0.01f, //nothing
    0.01f, //extreme
    0.005f, //earth
  };

  enum SCENE
  {
    ICE,
    VOYAGER,
    NOTHING,
    EXTREME,
    EARTH,
    COUNT
  };

  enum SPEC
  {
    VIZ,
    GAM,
    NEU,
    COUNT
  };

  int cur_scene_i;
  int next_scene_i;
  int cur_spec_i;
  int[,] layers;
  string[] spec_names;
  string[] scene_names;
  string[,] layer_names;
  GameObject[,] scene_groups;
  float[,] ta; //"time alive" (timed amt in scene/spectrum pairs)
  float scan_t = 5f; //time required before you can consider a spectrum "scanned"
  int default_layer;

  bool mouse_captured;
  bool mouse_just_captured;
  float mouse_x;
  float mouse_y;

  float in_portal_motion;
  float out_portal_motion;
  float max_portal_motion;

  float in_fail_motion;
  float out_fail_motion;
  float max_fail_motion;

  float gaze_t_max; //The time required to confirm a gaze
  float gaze_t_max_numb; //The time required to wait before confirming another gaze
  float gaze_t_since; //if positive, time since entered. if negative, time since exited.
  float gaze_t_in; //grows to max when in, shrinks to 0 when out
  float gaze_t_run; //grows while not fully out. 0 when fully out
  float gaze_t_numb; //countdown when distance should be ignored

  float spec_t_max; //The time required to select a button
  float spec_t_max_numb; //The time required to wait before selecting another button
  float spec_t_since; //if positive, time since entered. if negative, time since exited.
  float spec_t_in; //grows to max when in, shrinks to 0 when out
  float spec_t_run; //grows while not fully out. 0 when fully out
  float spec_t_numb; //countdown when distance should be ignored

  Vector3 gaze_pt;
  Vector3 anti_gaze_pt;
  Vector2 cam_euler;
  Vector2 gaze_cam_euler;
  Vector2 anti_gaze_cam_euler;
  Vector2 spec_euler;

  Vector2 getEuler(Vector3 v)
  {
    float plane_dist = new Vector2(v.x, v.z).magnitude;
    return new Vector2(Mathf.Atan2(v.y, plane_dist), -1 * (Mathf.Atan2(v.z, v.x) - Mathf.PI / 2));
  }

  Vector2 getCamEuler(Vector3 v)
  {
    return getEuler(v - main_camera.transform.position);
  }

  Quaternion rotationFromEuler(Vector2 euler)
  {
    return Quaternion.Euler(-Mathf.Rad2Deg * euler.x, Mathf.Rad2Deg * euler.y, 0);
  }

  void Start()
  {
    scene_names = new string[(int)SCENE.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SCENE.ICE:
          name = "Ice";
          break;
        case (int)SCENE.VOYAGER:
          name = "Voyager";
          break;
        case (int)SCENE.NOTHING:
          name = "Nothing";
          break;
        case (int)SCENE.EXTREME:
          name = "Extreme";
          break;
        case (int)SCENE.EARTH:
          name = "Earth";
          break;
      }
      scene_names[i] = name;
    }

    spec_names = new string[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      string name = "";
      switch(i)
      {
        case (int)SPEC.VIZ:
          name = "Viz";
          break;
        case (int)SPEC.GAM:
          name = "Gam";
          break;
        case (int)SPEC.NEU:
          name = "Neu";
          break;
      }
      spec_names[i] = name;
    }

    layer_names = new string[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        layer_names[i,j] = "Set_" + scene_names[i] + "_" + spec_names[j];

    scene_groups = new GameObject[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        scene_groups[i,j] = GameObject.Find(layer_names[i,j]);

    layers = new int[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        layers[i,j] = LayerMask.NameToLayer(layer_names[i,j]);

    voiceover_files = new string[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT+1; j++)
      {
        if(j == (int)SPEC.COUNT) voiceover_files[i,j] = "Voiceover/End/"+scene_names[i];
        else                     voiceover_files[i,j] = "Voiceover/"+spec_names[j]+"/"+scene_names[i];
      }
    }
    voiceovers = new AudioClip[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    voiceovers_played = new bool[(int)SCENE.COUNT,(int)SPEC.COUNT+1];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
    {
      for(int j = 0; j < (int)SPEC.COUNT+1; j++)
      {
        voiceovers[i,j] = Resources.Load<AudioClip>(voiceover_files[i,j]);
        voiceovers_played[i,j] = false;
        voiceovers_played[i,j] = true;
      }
    }

    //auto skip these
    voiceovers_played[(int)SCENE.ICE,(int)SPEC.NEU]     = true;
    voiceovers_played[(int)SCENE.ICE,(int)SPEC.GAM]     = true;
    voiceovers_played[(int)SCENE.NOTHING,(int)SPEC.NEU] = true;
    voiceovers_played[(int)SCENE.NOTHING,(int)SPEC.GAM] = true;
    voiceovers_played[(int)SCENE.EARTH,(int)SPEC.NEU]   = true;
    voiceovers_played[(int)SCENE.EARTH,(int)SPEC.GAM]   = true;

    string[,] skybox_files = new string[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        skybox_files[i,j] = "Skybox/"+spec_names[j]+"/"+skybox_file_names[i];

    helmet_colors = new Color[(int)SCENE.COUNT];
    helmet_colors[0] = scene0_helmet_color;
    helmet_colors[1] = scene1_helmet_color;
    helmet_colors[2] = scene2_helmet_color;
    helmet_colors[3] = scene3_helmet_color;
    helmet_colors[4] = scene4_helmet_color;

    ta = new float[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        ta[i,j] = 0;

    default_layer = LayerMask.NameToLayer("Default");

    camera_house = GameObject.Find("CameraHouse");
    main_camera = GameObject.Find("Main Camera");
    main_camera_skybox = main_camera.GetComponent<Skybox>();
    portal_projection = GameObject.Find("Portal_Projection");
    portal = GameObject.Find("Portal");
    portal_camera_next = GameObject.Find("Portal_Camera_Next");
    portal_camera_next_skybox = portal_camera_next.GetComponent<Skybox>();
    helmet = GameObject.Find("Helmet");
    helmet_light = GameObject.Find("Helmet_Light");
    helmet_light_light = helmet_light.GetComponent<Light>();
    cam_reticle = GameObject.Find("Cam_Reticle");
    cam_spinner = GameObject.Find("Cam_Spinner");
    gaze_projection = GameObject.Find("Gaze_Projection");
    gaze_reticle = GameObject.Find("Gaze_Reticle");
    spec_projection = GameObject.Find("Spec_Projection");
    spec_viz_reticle = GameObject.Find("Spec_Viz_Reticle");
    spec_gam_reticle = GameObject.Find("Spec_Gam_Reticle");
    spec_neu_reticle = GameObject.Find("Spec_Neu_Reticle");
    spec_sel_reticle = GameObject.Find("Spec_Sel_Reticle");
    eyeray = GameObject.Find("Ray");
    ar_group = GameObject.Find("AR");
    ar_camera_project = GameObject.Find("AR_Camera_Project");
    ar_camera_static = GameObject.Find("AR_Camera_Static");
    ar_alert = GameObject.Find("Alert");
    ar_timer = GameObject.Find("Timer");
    ar_timer_text = ar_timer.GetComponent<TextMesh>();
    //stars = GameObject.Find("Stars");
    //starsscale = GameObject.Find("StarsScale");

    ar_labels         = new GameObject[MAX_LABELS];
    ar_label_offsets  = new GameObject[MAX_LABELS];
    ar_label_texts    = new TextMesh[MAX_LABELS];
    ar_label_lines    = new LineRenderer[MAX_LABELS];

    //technically isn't connected to max labels, but as there's a label per line, it's at least upper bound
    ar_progresses       = new GameObject[MAX_LABELS];
    ar_progress_offsets = new GameObject[MAX_LABELS];
    ar_progress_lines   = new LineRenderer[MAX_LABELS];

    float lw;
    AnimationCurve curve;
    lw = 0.0001f;
    curve = new AnimationCurve();
    curve.AddKey(0, lw);
    curve.AddKey(1, lw);
    for(int i = 0; i < MAX_LABELS; i++)
    {
      ar_label_offsets[i] = (GameObject)Instantiate(ar_label_prefab);
      ar_label_offsets[i].transform.parent = ar_group.transform;
      ar_labels[i] = ar_label_offsets[i].transform.GetChild(0).gameObject;
      ar_label_texts[i] = ar_labels[i].GetComponent<TextMesh>();
      ar_label_lines[i] = ar_labels[i].GetComponent<LineRenderer>();

      ar_label_lines[i].widthCurve = curve;
      for(int j = 0; j < 3; j++)
        ar_label_lines[i].SetPosition(j, new Vector3(0, 0, 0));

      ar_progress_offsets[i] = (GameObject)Instantiate(ar_progress_prefab);
      ar_progress_offsets[i].transform.parent = ar_group.transform;
      ar_progresses[i] = ar_progress_offsets[i].transform.GetChild(0).gameObject;
      ar_progress_lines[i] = ar_progresses[i].GetComponent<LineRenderer>();

      ar_progress_lines[i].widthCurve = curve;
      for(int j = 0; j < 2; j++)
        ar_progress_lines[i].SetPosition(j, new Vector3(0, 0, 0));
    }

    ar_alert.SetActive(false);
    ar_timer.SetActive(false);

    icecube   = new GameObject[(int)SPEC.COUNT];
    voyager   = new GameObject[(int)SPEC.COUNT];
    pluto     = new GameObject[(int)SPEC.COUNT];
    vearth    = new GameObject[(int)SPEC.COUNT];
    milky     = new GameObject[(int)SPEC.COUNT];
    nearth    = new GameObject[(int)SPEC.COUNT];
    blackhole = new GameObject[(int)SPEC.COUNT];
    esun      = new GameObject[(int)SPEC.COUNT];
    earth     = new GameObject[(int)SPEC.COUNT];
    for(int i = 0; i < (int)SPEC.COUNT; i++)
    {
      icecube[i]   = GameObject.Find("Icecube_"   + spec_names[i]);
      voyager[i]   = GameObject.Find("Voyager_"   + spec_names[i]);
      pluto[i]     = GameObject.Find("Pluto_"     + spec_names[i]);
      vearth[i]    = GameObject.Find("VEarth_"    + spec_names[i]);
      milky[i]     = GameObject.Find("Milky_"     + spec_names[i]);
      nearth[i]    = GameObject.Find("NEarth_"    + spec_names[i]);
      blackhole[i] = GameObject.Find("BlackHole_" + spec_names[i]);
      esun[i]      = GameObject.Find("ESun_"      + spec_names[i]);
      earth[i]     = GameObject.Find("Earth_"     + spec_names[i]);
    }

    scene_centers[(int)SCENE.ICE]     = icecube[  0].transform.position;
    scene_centers[(int)SCENE.VOYAGER] = voyager[  0].transform.position;
    scene_centers[(int)SCENE.NOTHING] = milky[    0].transform.position;
    scene_centers[(int)SCENE.EXTREME] = blackhole[0].transform.position;
    scene_centers[(int)SCENE.EARTH]   = earth[    0].transform.position;

    alpha_id = Shader.PropertyToID("alpha");
    flash_alpha = 0;
    time_mod_twelve_pi_id = Shader.PropertyToID("time_mod_twelve_pi");
    time_mod_twelve_pi = 0;
    jitter_id = Shader.PropertyToID("jitter");
    jitter = 0;
    jitter_countdown = 0;
    jitter_state = 0;

    voiceover_audiosource = GameObject.Find("Script").AddComponent<AudioSource>();
    voiceover_was_playing = false;
    //sfx_audiosource = GameObject.Find("Script").AddComponent<AudioSource>();

    default_portal_scale = portal.transform.localScale;
    default_portal_position = portal.transform.position;

    default_satellite_position = new Vector3(4f, 1.5f, 10);
    satellite_position = default_satellite_position;
    satellite_velocity = new Vector3(0, 0, -0.5f);
    voyager[0].transform.position = satellite_position;

    default_look_ahead = new Vector3(0, 0, 1);
    look_ahead = default_look_ahead;
    lazy_look_ahead = default_look_ahead;
    very_lazy_look_ahead = default_look_ahead;
    player_head = new Vector3(0, 2, 0);

    next_scene_i = (int)SCENE.ICE;
    next_scene_i = (int)SCENE.EARTH;
    cur_scene_i = next_scene_i;
    next_scene_i = (next_scene_i + 1) % ((int)SCENE.COUNT);
    cur_spec_i = 0;

    mouse_captured = false;
    mouse_just_captured = true;
    mouse_x = Screen.width / 2;
    mouse_y = Screen.height / 2;

    camera_house.transform.rotation = Quaternion.Euler((mouse_y - Screen.height / 2) * -2, (mouse_x - Screen.width / 2) * 2, 0);

    in_portal_motion = 0;
    out_portal_motion = 0;
    max_portal_motion = 1;

    in_fail_motion = 0;
    out_fail_motion = 0;
    max_fail_motion = 1;

    gaze_t_max = 2f;
    gaze_t_max_numb = 4f;
    gaze_t_since = 0;
    gaze_t_in = 0;
    gaze_t_run = 0;
    gaze_t_numb = 0;

    spec_t_max = 1f;
    spec_t_max_numb = 5f;
    spec_t_since = 0;
    spec_t_in = 0;
    spec_t_run = 0;
    spec_t_numb = 0;

    gaze_pt = new Vector3(1f, .8f, -1f).normalized;

    gaze_pt *= 1000;
    cam_euler = getCamEuler(cam_reticle.transform.position);
    gaze_cam_euler = getCamEuler(gaze_pt);
    anti_gaze_pt = (gaze_pt * -1) + new Vector3(50f, 0, 0);
    anti_gaze_cam_euler = getCamEuler(anti_gaze_pt);

    eyeray.GetComponent<LineRenderer>().SetPosition(0, anti_gaze_pt);
    eyeray.GetComponent<LineRenderer>().SetPosition(1, gaze_pt);

    spec_euler = cam_euler;
    spec_euler.x = -3.141592f / 3f;
    spec_projection.transform.rotation = rotationFromEuler(spec_euler);

    skyboxes = new Material[(int)SCENE.COUNT, (int)SPEC.COUNT];
    for(int i = 0; i < (int)SCENE.COUNT; i++)
      for(int j = 0; j < (int)SPEC.COUNT; j++)
        skyboxes[i,j] = Resources.Load<Material>(skybox_files[i,j]);
    main_camera_skybox.material = skyboxes[cur_scene_i, cur_spec_i];
    portal_camera_next_skybox.material = skyboxes[next_scene_i, (int)SPEC.VIZ];

/*
    //stars
    GameObject[] star_groups;
    GameObject star;
    Vector3[] star_positions;
    Vector3 starpos;

    int n_stars = 0;//100000;
    int n_groups = (int)Mathf.Ceil(n_stars / 1000);
    int n_stars_in_group;
    star_groups = new GameObject[n_groups];
    star = (GameObject)Instantiate(star_prefab);

    //gen positions
    star_positions = new Vector3[n_stars];
    for(int i = 0; i < n_stars; i++)
    {
      bool good_star = false;
      starpos = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
      good_star = (starpos.sqrMagnitude < 1f);
      while(!good_star)
      {
        starpos = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
        good_star = (starpos.sqrMagnitude < 1f);
      }
      starpos = starpos.normalized;
      star_positions[i] = starpos;
    }

    //morph positions
    for(int n = 0; n < 2; n++)
    {
      for(int i = 0; i < n_stars; i++)
      {
        Vector3 delta = new Vector3(0, 0, 0);
        Vector3 dist;
        for(int j = 0; j < n_stars; j++)
        {
          if(j != i)
          {
            dist = star_positions[j] - star_positions[i];
            if(dist.sqrMagnitude < 0.1)
            {
              delta += dist * (0.0001f / dist.sqrMagnitude);
            }
          }
        }
        star_positions[i] = (star_positions[i] + delta).normalized;
      }
    }

    //gen assets
    for(int i = 0; i < n_groups; i++)
    {
      n_stars_in_group = Mathf.Min(1000, n_stars);
      CombineInstance[] combine = new CombineInstance[n_stars_in_group];

      for(int j = 0; j < n_stars_in_group; j++)
      {
        starpos = star_positions[i * n_stars_in_group + j];
        starpos *= Mathf.Lerp(0f, 30f, Random.Range(0f, 1f));

        star.transform.position = starpos;
        star.transform.rotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
        float ss = 0.1f;
        star.transform.localScale = new Vector3(ss, ss, ss);

        combine[j].mesh = star.transform.GetComponent<MeshFilter>().mesh;
        combine[j].transform = star.transform.localToWorldMatrix;
      }

      star_groups[i] = (GameObject)Instantiate(star_prefab);
      star_groups[i].transform.parent = starsscale.transform;
      //star_groups[i].layer = stars_layer;
      star_groups[i].transform.localPosition = new Vector3(0, 0, 0);
      star_groups[i].transform.localRotation = Quaternion.Euler(0, 0, 0);
      star_groups[i].transform.localScale = new Vector3(1, 1, 1);
      star_groups[i].transform.GetComponent<MeshFilter>().mesh = new Mesh();
      star_groups[i].transform.GetComponent<MeshFilter>().mesh.CombineMeshes(combine);

      n_stars -= n_stars_in_group;
    }
    Destroy(star, 0f);
*/

    SetupScene();
  }

  void SetupScene()
  {
    SetSpec((int)SPEC.VIZ);
    spec_t_numb = spec_t_max_numb;

    main_camera_skybox.material = skyboxes[cur_scene_i, cur_spec_i];

    AnimationCurve curve;
    float lw;
    lw = 0.0001f;
    curve = new AnimationCurve();
    curve.AddKey(0, lw);
    curve.AddKey(1, lw);
    for(int i = 0; i < MAX_LABELS; i++)
    {
      ar_label_lines[i].widthCurve = curve;
      for(int j = 0; j < 3; j++)
        ar_label_lines[i].SetPosition(j, new Vector3(0, 0, 0));
      ar_label_texts[i].text = "";

      ar_progress_lines[i].widthCurve = curve;
      for(int j = 0; j < 2; j++)
        ar_progress_lines[i].SetPosition(j, new Vector3(0, 0, 0));
    }

    int label_i = 0;
    switch(cur_scene_i)
    {
      case (int)SCENE.ICE:
        ar_label_offsets[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_label_offsets[label_i].transform.position = icecube[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Ice Cube";
        ar_labels[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_labels[label_i].transform.localPosition = new Vector3(10f, 10f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-8,   0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-10,  0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-11, -5, 0));
        label_i++;

        spec_projection.SetActive(false);
        gaze_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(gaze_cam_euler);
        break;

      case (int)SCENE.VOYAGER:
        ar_label_offsets[label_i].transform.localScale = new Vector3(5f, 5f, 5f);
        ar_label_offsets[label_i].transform.position = voyager[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Voyager";
        ar_labels[label_i].transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        ar_labels[label_i].transform.localPosition = new Vector3(1f, 1f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 0.2f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-8, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-10, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-11, -5, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(50f, 50f, 50f);
        ar_label_offsets[label_i].transform.position = pluto[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Pluto";
        ar_labels[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_labels[label_i].transform.localPosition = new Vector3(2f, 2f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 2f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-5,  0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-7,  0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-8, -2, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(20f, 20f, 20f);
        ar_label_offsets[label_i].transform.position = vearth[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Earth";
        ar_labels[label_i].transform.localScale = new Vector3(5f, 5f, 5f);
        ar_labels[label_i].transform.localPosition = new Vector3(2f, 2f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 2f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-5,  0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-7,  0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-8, -2, 0));
        label_i++;

        spec_projection.SetActive(true);
        break;

      case (int)SCENE.NOTHING:
        ar_label_offsets[label_i].transform.localScale = new Vector3(50f, 50f, 50f);
        ar_label_offsets[label_i].transform.position = milky[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Milky Way";
        ar_labels[label_i].transform.localScale = new Vector3(3f, 3f, 3f);
        ar_labels[label_i].transform.localPosition = new Vector3(1f, 1f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 1f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-10, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-12, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-17, -10, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(50f, 50f, 50f);
        ar_label_offsets[label_i].transform.position = nearth[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Earth";
        ar_labels[label_i].transform.localScale = new Vector3(3f, 3f, 3f);
        ar_labels[label_i].transform.localPosition = new Vector3(1f, 1f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 1f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-10, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-12, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-17, -10, 0));
        label_i++;

        break;

      case (int)SCENE.EXTREME:
        float bar_y = -3;
        float bar_x = -11;

        ar_label_offsets[label_i].transform.localScale = new Vector3(200f, 200f, 200f);
        ar_label_offsets[label_i].transform.position = blackhole[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_progress_offsets[label_i].transform.localScale = ar_label_offsets[label_i].transform.localScale;
        ar_progress_offsets[label_i].transform.position   = ar_label_offsets[label_i].transform.position;
        ar_progress_offsets[label_i].transform.rotation   = ar_label_offsets[label_i].transform.rotation;
        ar_label_texts[label_i].text = "Visible Scan";
        ar_labels[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_labels[label_i].transform.localPosition = new Vector3(-1.2f, 1f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        ar_progresses[label_i].transform.localScale    = ar_labels[label_i].transform.localScale;
        ar_progresses[label_i].transform.localPosition = ar_labels[label_i].transform.localPosition;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(11, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(14, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(16, -8, 0));
        lw = 10f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_progress_lines[label_i].widthCurve = curve;
        ar_progress_lines[label_i].SetPosition(0, new Vector3(bar_x, bar_y, 0));
        ar_progress_lines[label_i].SetPosition(1, new Vector3(bar_x, bar_y, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(200f, 200f, 200f);
        ar_label_offsets[label_i].transform.position = blackhole[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_progress_offsets[label_i].transform.localScale = ar_label_offsets[label_i].transform.localScale;
        ar_progress_offsets[label_i].transform.position   = ar_label_offsets[label_i].transform.position;
        ar_progress_offsets[label_i].transform.rotation   = ar_label_offsets[label_i].transform.rotation;
        ar_label_texts[label_i].text = "Gamma Scan";
        ar_labels[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_labels[label_i].transform.localPosition = new Vector3(-1.5f, 0f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        ar_progresses[label_i].transform.localScale    = ar_labels[label_i].transform.localScale;
        ar_progresses[label_i].transform.localPosition = ar_labels[label_i].transform.localPosition;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(11, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(14, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(16, 0, 0));
        lw = 10f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_progress_lines[label_i].widthCurve = curve;
        ar_progress_lines[label_i].SetPosition(0, new Vector3(bar_x, bar_y, 0));
        ar_progress_lines[label_i].SetPosition(1, new Vector3(bar_x, bar_y, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(200f, 200f, 200f);
        ar_label_offsets[label_i].transform.position = blackhole[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_progress_offsets[label_i].transform.localScale = ar_label_offsets[label_i].transform.localScale;
        ar_progress_offsets[label_i].transform.position   = ar_label_offsets[label_i].transform.position;
        ar_progress_offsets[label_i].transform.rotation   = ar_label_offsets[label_i].transform.rotation;
        ar_label_texts[label_i].text = "Neutrino Scan";
        ar_labels[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_labels[label_i].transform.localPosition = new Vector3(-1.2f, -1f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        ar_progresses[label_i].transform.localScale    = ar_labels[label_i].transform.localScale;
        ar_progresses[label_i].transform.localPosition = ar_labels[label_i].transform.localPosition;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(11, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(14, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(16, 8, 0));
        lw = 10f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_progress_lines[label_i].widthCurve = curve;
        ar_progress_lines[label_i].SetPosition(0, new Vector3(bar_x, bar_y, 0));
        ar_progress_lines[label_i].SetPosition(1, new Vector3(bar_x, bar_y, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(200f, 200f, 200f);
        ar_label_offsets[label_i].transform.position = blackhole[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Black Hole";
        ar_labels[label_i].transform.localScale = new Vector3(10f, 10f, 10f);
        ar_labels[label_i].transform.localPosition = new Vector3(1f, 1f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3(-11, 0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-14, 0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-16, -8, 0));
        label_i++;

        for(int j = 0; j < (int)SPEC.COUNT; j++)
          ta[(int)SCENE.EXTREME,j] = 0;
        for(int j = 0; j < (int)SPEC.COUNT+1; j++)
          voiceovers_played[(int)SCENE.EXTREME,j] = false;

        gaze_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);
        portal_projection.transform.rotation = rotationFromEuler(anti_gaze_cam_euler);
        ar_alert.SetActive(true);
        ar_timer.SetActive(true);
        timer_t = 0;
        alert_t = 0;
        break;

      case (int)SCENE.EARTH:
        ar_label_offsets[label_i].transform.localScale = new Vector3(8f, 8f, 8f);
        ar_label_offsets[label_i].transform.position = earth[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Ice Cube";
        ar_labels[label_i].transform.localScale = new Vector3(5f, 5f, 5f);
        ar_labels[label_i].transform.localPosition = new Vector3(2.25f, 3f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3( -8,  0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-12,  0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-14, -4, 0));
        label_i++;

        ar_label_offsets[label_i].transform.localScale = new Vector3(8f, 8f, 8f);
        ar_label_offsets[label_i].transform.position = esun[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        ar_label_texts[label_i].text = "Sun";
        ar_labels[label_i].transform.localScale = new Vector3(9f, 9f, 9f);
        ar_labels[label_i].transform.localPosition = new Vector3(17f, 10f, 0f);
        ar_labels[label_i].transform.localScale /= ar_label_offsets[label_i].transform.localScale.x;
        lw = 3f;
        curve = new AnimationCurve();
        curve.AddKey(0, lw);
        curve.AddKey(1, lw);
        ar_label_lines[label_i].widthCurve = curve;
        ar_label_lines[label_i].SetPosition(0, new Vector3( -4,  0, 0));
        ar_label_lines[label_i].SetPosition(1, new Vector3(-12,  0, 0));
        ar_label_lines[label_i].SetPosition(2, new Vector3(-14, -4, 0));
        label_i++;

        ar_alert.SetActive(false);
        ar_timer.SetActive(false);
        spec_projection.SetActive(false);
        break;
    }

    helmet_light_light.color = helmet_colors[cur_scene_i];

    if(!voiceovers_played[cur_scene_i,(int)SPEC.VIZ])
    {
      if(voiceover_audiosource.isPlaying) voiceover_audiosource.Stop();
      voiceover_audiosource.clip = voiceovers[cur_scene_i,(int)SPEC.VIZ];
      voiceover_audiosource.Play();
      voiceover_was_playing = true;
      voiceovers_played[cur_scene_i,(int)SPEC.VIZ] = true;
    }
  }

  void UpdateScene()
  {
    /*
      //HACK
      ar_label_offsets[1].transform.localScale = new Vector3(label_offset_local_scale, label_offset_local_scale, label_offset_local_scale); //scales the offset- NOT THE SIZE OF ANYTHING (this is dumb)
      ar_label_offsets[1].transform.position = esun[0].transform.position;
      ar_label_offsets[1].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[1].transform.position));
      ar_label_texts[1].text = "Sun";
      ar_labels[1].transform.localScale = new Vector3(label_local_scale, label_local_scale, label_local_scale); //scales the thing
      ar_labels[1].transform.localPosition = label_local_position; //move the whole thing
      ar_labels[1].transform.localScale /= ar_label_offsets[1].transform.localScale.x;
      ar_label_lines[1].SetPosition(0, line_0);
      ar_label_lines[1].SetPosition(1, line_1);
      ar_label_lines[1].SetPosition(2, line_2);
    */

    ta[cur_scene_i,cur_spec_i] += Time.deltaTime;

    int label_i = 0;
    switch(cur_scene_i)
    {
      case (int)SCENE.ICE:
        break;
      case (int)SCENE.VOYAGER:
        for(int i = 1; i < voyager.Length; i++)
        {
          voyager[i].transform.position = voyager[0].transform.position;
        }
        ar_label_offsets[label_i].transform.position = voyager[0].transform.position;
        ar_label_offsets[label_i].transform.rotation = rotationFromEuler(getCamEuler(ar_label_offsets[label_i].transform.position));
        label_i++;
        break;
      case (int)SCENE.NOTHING:
        break;
      case (int)SCENE.EXTREME:
        alert_t += Time.deltaTime;
        timer_t += Time.deltaTime;
        if(Mathf.Floor(alert_t) % 2 == 1)
          ar_alert.SetActive(false);
        else
          ar_alert.SetActive(true);
        float seconds_left = 60 - timer_t;
        if(seconds_left > 0)
        {
          ar_timer_text.text = "00:" + Mathf.Floor(seconds_left) + ":" + Mathf.Floor((seconds_left - Mathf.Floor(seconds_left)) * 100);
          float bar_y = -3;
          float bar_x = -11;
          float bar_w = 23;
          for(int i = 0; i < (int)SPEC.COUNT; i++)
            ar_progress_lines[i].SetPosition(1, new Vector3(bar_x+bar_w*Mathf.Min(1,(ta[cur_scene_i,i]/scan_t)), bar_y, 0));
        }
        else if(in_fail_motion == 0)
        {
          in_fail_motion = 0.0001f;
          ar_timer_text.text = "XX:XX:XX";
        }
        break;
      case (int)SCENE.EARTH:
        break;
    }
  }

  void SetSpec(int spec)
  {
    cur_spec_i = spec;
    switch(spec)
    {
      case (int)SPEC.GAM: spec_sel_reticle.transform.position = spec_gam_reticle.transform.position; break;
      case (int)SPEC.VIZ: spec_sel_reticle.transform.position = spec_viz_reticle.transform.position; break;
      case (int)SPEC.NEU: spec_sel_reticle.transform.position = spec_neu_reticle.transform.position; break;
    }

    main_camera.GetComponent<Camera>().cullingMask = (1 << layers[cur_scene_i, cur_spec_i]) | (1 << default_layer);
    portal_camera_next.GetComponent<Camera>().cullingMask = (1 << layers[next_scene_i, (int)SPEC.VIZ]);
    main_camera_skybox.material = skyboxes[cur_scene_i, cur_spec_i];
    portal_camera_next_skybox.material = skyboxes[next_scene_i, (int)SPEC.VIZ];
  }

  void Update()
  {
    float aspect = main_camera.GetComponent<Camera>().aspect;
    float fov = main_camera.GetComponent<Camera>().fieldOfView;
    ar_camera_project.GetComponent<Camera>().aspect = aspect;
    ar_camera_static.GetComponent<Camera>().aspect = aspect;
    portal_camera_next.GetComponent<Camera>().aspect = aspect;
    ar_camera_project.GetComponent<Camera>().fieldOfView = fov;
    ar_camera_static.GetComponent<Camera>().fieldOfView = fov;
    portal_camera_next.GetComponent<Camera>().fieldOfView = fov;

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

    if(in_portal_motion > 0) in_portal_motion += Time.deltaTime * 0.8f;
    if(in_portal_motion > max_portal_motion)
    {
      out_portal_motion = in_portal_motion-max_portal_motion;
      if(out_portal_motion <= 0) out_portal_motion = 0.00001f;
      in_portal_motion = 0;
      cur_scene_i = next_scene_i;
      next_scene_i = (next_scene_i + 1) % ((int)SCENE.COUNT);
      SetupScene();
    }
    if(out_portal_motion > 0)                 out_portal_motion += Time.deltaTime;
    if(out_portal_motion > max_portal_motion) out_portal_motion = 0;

    if(in_portal_motion > 0)
    {
      float t = in_portal_motion / (float)max_portal_motion;
      portal.transform.localPosition = new Vector3(default_portal_position.x, default_portal_position.y, Mathf.Lerp(default_portal_position.z, 0, t * t * t * t * t));
      float engulf = t - 1;
      engulf *= -engulf;
      engulf += 1;
      engulf /= 2;
      portal.transform.localScale = new Vector3(default_portal_scale.x * engulf, default_portal_scale.y * engulf, default_portal_scale.z * engulf);
    }
    else
    {
      portal.transform.localPosition = default_portal_position;
      portal.transform.localScale = new Vector3(0, 0, 0);
    }

    if(in_fail_motion > 0) in_fail_motion += Time.deltaTime * 0.2f;
    if(in_fail_motion > max_fail_motion)
    {
      out_fail_motion = in_fail_motion-max_fail_motion;
      if(out_fail_motion <= 0) out_fail_motion = 0.00001f;
      in_fail_motion = 0;
      next_scene_i = cur_scene_i;
      scene_rots[next_scene_i] = 0;
      SetupScene();
    }
    if(out_fail_motion > 0)               out_fail_motion += Time.deltaTime;
    if(out_fail_motion > max_fail_motion) out_fail_motion = 0;

    UpdateScene();

    if(mouse_captured)
    {
      float in_x = Input.GetAxis("Mouse X") * 10;
      float in_y = Input.GetAxis("Mouse Y") * 10;
      if(!mouse_just_captured)
      {
        mouse_x += in_x;
        mouse_y += in_y;
      }
      else
      {
        if(in_x != 0 || in_y != 0)
          mouse_just_captured = false;
      }
    }

    Vector3 offset = new Vector3(
                       -main_camera.transform.localPosition.x,
                       -main_camera.transform.localPosition.y,
                       -main_camera.transform.localPosition.z);
    camera_house.transform.localPosition = offset + player_head;
    if(cur_scene_i == (int)SCENE.EXTREME)
      camera_house.transform.rotation = Quaternion.Euler((mouse_y - Screen.height / 2) * -2 + Random.Range(-extreme_camera_shake, extreme_camera_shake), (mouse_x - Screen.width / 2) * 2 + Random.Range(-extreme_camera_shake, extreme_camera_shake), 0 + Random.Range(-extreme_camera_shake, extreme_camera_shake));
    else
      camera_house.transform.rotation = Quaternion.Euler((mouse_y - Screen.height / 2) * -2, (mouse_x - Screen.width / 2) * 2, 0);

    look_ahead = main_camera.transform.rotation * default_look_ahead;
    lazy_look_ahead = Vector3.Lerp(lazy_look_ahead, look_ahead, 0.1f);
    very_lazy_look_ahead = Vector3.Lerp(very_lazy_look_ahead, look_ahead, 0.002f);
    if(cur_scene_i == (int)SCENE.EXTREME)
      helmet.transform.position = main_camera.transform.position + new Vector3(Random.Range(-extreme_helmet_shake, extreme_helmet_shake), Random.Range(-extreme_helmet_shake, extreme_helmet_shake), Random.Range(-extreme_helmet_shake, extreme_helmet_shake));
    else
      helmet.transform.position = main_camera.transform.position;
    helmet.transform.rotation = rotationFromEuler(getEuler(lazy_look_ahead));

    cam_euler = getCamEuler(cam_reticle.transform.position);
    spec_euler = getEuler(very_lazy_look_ahead);
    spec_euler.x = -3.141592f / 3f;
    spec_projection.transform.rotation = rotationFromEuler(spec_euler);

    satellite_position += satellite_velocity * Time.deltaTime;
    if(cur_scene_i != 1)
      satellite_position = default_satellite_position;
    voyager[0].transform.position = satellite_position;

    if(in_portal_motion > 0)
    {
      float t = (float)in_portal_motion / max_portal_motion;
      flash_alpha = t * t * t * t * t;
    }
    else if(out_portal_motion > 0)
    {
      flash_alpha = 1.0f - ((float)out_portal_motion / max_portal_motion);
    }
    else if(in_fail_motion > 0)
    {
      float t = (float)in_fail_motion / max_fail_motion;
      flash_alpha = t * t * t * t * t;
    }
    else if(out_fail_motion > 0)
    {
      flash_alpha = 1.0f - ((float)out_fail_motion / max_fail_motion);
    }
    else
      flash_alpha = 0;
    flash_alpha *= 1.1f;
    if(flash_alpha > 1)
      flash_alpha = 1;
    flash_alpha = flash_alpha * flash_alpha;
    flash_alpha = flash_alpha * flash_alpha;
    alpha_material.SetFloat(alpha_id, flash_alpha);

    float shrink;
    float rot;

    shrink = (float)gaze_t_in / (float)gaze_t_max;
    rot = ((float)gaze_t_run / gaze_t_max) * 5 * 360.0f;

    cam_spinner.transform.localScale = new Vector3(shrink, shrink, shrink);
    cam_spinner.transform.localRotation = Quaternion.Euler(0, 0, rot);

    float distance = Vector3.Distance(gaze_reticle.transform.position, cam_reticle.transform.position);
    if(gaze_t_numb <= 0 && distance < 0.3 && voiceovers_played[cur_scene_i,(int)SPEC.COUNT])
    {
      if(gaze_t_since < 0)        gaze_t_since = Time.deltaTime;
      else                        gaze_t_since += Time.deltaTime;
      if(gaze_t_in < gaze_t_max)  gaze_t_in += Time.deltaTime;
      if(gaze_t_in >= gaze_t_max) gaze_t_numb = gaze_t_max_numb;

      //advance
      if(gaze_t_in >= gaze_t_max)
      {
        if(in_portal_motion == 0 && out_portal_motion == 0)
        {
          in_portal_motion = Time.deltaTime;
          scene_rots[next_scene_i] = 0; //set rot at start of scene transition
        }
      }
    }
    else
    {
      if(gaze_t_since > 0) gaze_t_since = -Time.deltaTime;
      else                 gaze_t_since -= Time.deltaTime;
      if(gaze_t_in > 0)    gaze_t_in -= Time.deltaTime;
    }
    if(gaze_t_in > 0)   gaze_t_run += Time.deltaTime;
    else                gaze_t_run = 0;
    if(gaze_t_numb > 0) gaze_t_numb -= Time.deltaTime;

    float distance_viz = Vector3.Distance(spec_viz_reticle.transform.position, cam_reticle.transform.position);
    float distance_gam = Vector3.Distance(spec_gam_reticle.transform.position, cam_reticle.transform.position);
    float distance_neu = Vector3.Distance(spec_neu_reticle.transform.position, cam_reticle.transform.position);
    if(spec_projection.activeInHierarchy && spec_t_numb <= 0 && (distance_gam < 0.5 || distance_viz < 0.5 || distance_neu < 0.5))
    {
      if(spec_t_since < 0)        spec_t_since = Time.deltaTime;
      else                        spec_t_since += Time.deltaTime;
      if(spec_t_in < spec_t_max)  spec_t_in += Time.deltaTime;
      if(spec_t_in >= spec_t_max) spec_t_numb = spec_t_max_numb;

      if(spec_t_in >= spec_t_max)
      {
        int old_spec = cur_spec_i;
        if(distance_gam <= distance_viz && distance_gam <= distance_neu) SetSpec((int)SPEC.GAM);
        if(distance_viz <= distance_gam && distance_viz <= distance_neu) SetSpec((int)SPEC.VIZ);
        if(distance_neu <= distance_gam && distance_neu <= distance_viz) SetSpec((int)SPEC.NEU);

        if(old_spec != cur_spec_i)
        {
          if(!voiceovers_played[cur_scene_i,cur_spec_i])
          {
            if(voiceover_audiosource.isPlaying) voiceover_audiosource.Stop();
            voiceover_audiosource.clip = voiceovers[cur_scene_i,cur_spec_i];
            voiceover_audiosource.Play();
            voiceover_was_playing = true;
            voiceovers_played[cur_scene_i,cur_spec_i] = true;
          }
        }
      }
    }
    else
    {
      if(spec_t_since > 0) spec_t_since = -Time.deltaTime;
      else                 spec_t_since -= Time.deltaTime;
      if(spec_t_in > 0)    spec_t_in -= Time.deltaTime;
    }
    if(spec_t_in > 0)   spec_t_run += Time.deltaTime;
    else                spec_t_run = 0;
    if(spec_t_numb > 0) spec_t_numb -= Time.deltaTime;

    scene_rots[cur_scene_i] += scene_rot_deltas[cur_scene_i] * Time.deltaTime;
    while(scene_rots[cur_scene_i] > 3.14159265f * 2.0f) scene_rots[cur_scene_i] -= (3.14159265f * 2.0f);
    scene_rots[next_scene_i] += scene_rot_deltas[next_scene_i] * Time.deltaTime;
    while(scene_rots[next_scene_i] > 3.14159265f * 2.0f) scene_rots[next_scene_i] -= (3.14159265f * 2.0f);

    scene_groups[cur_scene_i, cur_spec_i].transform.position = new Vector3(0, 0, 0);
    scene_groups[cur_scene_i, cur_spec_i].transform.rotation = Quaternion.Euler(0f, 0f, 0f);

    scene_groups[cur_scene_i, cur_spec_i].transform.Translate(scene_centers[cur_scene_i]);
    scene_groups[cur_scene_i, cur_spec_i].transform.Rotate(0f, Mathf.Rad2Deg * scene_rots[cur_scene_i], 0f);
    scene_groups[cur_scene_i, cur_spec_i].transform.Translate(-scene_centers[cur_scene_i]);
    main_camera_skybox.material.SetFloat("_Rotation", -Mathf.Rad2Deg * scene_rots[cur_scene_i]);

    time_mod_twelve_pi = (time_mod_twelve_pi + Time.deltaTime) % (12.0f * 3.1415926535f);
    Shader.SetGlobalFloat(time_mod_twelve_pi_id, time_mod_twelve_pi);
    jitter_countdown -= Time.deltaTime;
    if(jitter_countdown <= 0.0f)
    {
      if(jitter_state == 1)
      {
        jitter_state = 0;
        jitter_countdown = Random.Range(jitter_min_downtime, jitter_max_downtime);
      }
      else
      {
        jitter_state = 1;
        jitter_countdown = Random.Range(jitter_min_uptime, jitter_max_uptime);
      }
    }
    if(jitter_state == 1 && cur_scene_i == (int)SCENE.EXTREME)
      jitter += Random.Range(-0.1f, 0.1f);
    else
      jitter = 0;
    Shader.SetGlobalFloat(jitter_id, jitter);

    if(voiceover_was_playing)
    {
      if(!voiceover_audiosource.isPlaying)
      {
        bool play_end = !voiceovers_played[cur_scene_i,(int)SPEC.COUNT];
        for(int i = 0; play_end && i < (int)SPEC.COUNT; i++)
        {
          if(!voiceovers_played[cur_scene_i,i]) play_end = false;
        }
        if(play_end)
        {
          voiceover_audiosource.clip = voiceovers[cur_scene_i,(int)SPEC.COUNT];
          voiceover_audiosource.Play();
          voiceover_was_playing = true;
          voiceovers_played[cur_scene_i,(int)SPEC.COUNT] = true;
        }
      }
    }

  }
}

