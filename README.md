# ICECUBEVR

* Player Events (Play choices and actions)
headset_on - this sets the 0 time for seconds_from_launch)
language_selected (string langugage, float seconds_from_launch)
start(float seconds_from_launch)
viewport_data ([float seconds_from_launch, float pos, float rototation]) 
object_selected (string gaze_point_name)


* Progression Events (An achievement is made, a goal is met, time advaces)
scene_change (string scene_name)
object_assigned (string object) 


* Feedback Events (The system is communicating something to the player formatively)
script_audio_started (int/string clip_identifier)
script_audio_complete (int/string clip_identifier)
caption_displayed (string caption)
new_object_displayed (bool has_the_indicator, string object, float pos, float rotation)
