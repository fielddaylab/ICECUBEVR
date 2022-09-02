# ICECUBEVR

Player Events (Play choices and actions)
* headset_on - this sets the 0 time for seconds_from_launch)
* language_selected (string langugage, float seconds_from_launch)
* start(float seconds_from_launch)
* viewport_data ([float seconds_from_launch, float pos, float rototation]) 
* object_selected (float seconds_from_launch, string gaze_point_name, [string remaining_assinged_objects])


Progression Events (An achievement is made, a goal is met, time advaces)
* scene_change (float seconds_from_launch, string scene_name)
* object_assigned (float seconds_from_launch, string object) - Something in the script just said, "you should look at ...."

Feedback Events (The system is communicating something to the player formatively)
* script_audio_started (float seconds_from_launch, int/string clip_identifier)
* script_audio_complete (float seconds_from_launch, int/string clip_identifier)
* caption_displayed (float seconds_from_launch, string caption)
* new_object_displayed (float seconds_from_launch, bool has_the_indicator, string object, float pos, float rotation)
