1) Simply replace the original shader with the 3y3net/SnowCover shader
2) Set snow amount and snow color parameter to whatever you wish

DONE!

If you wish to change the snow amount and/or snow color in runtime proceed with step 1 and then add the CoverAPI script to your 3D model.
There are two public methods you can invoke from other script or third party assets:

public void SetSnow(float value)
Set the snow amount from 0 to 20.

public void SetColor(Color color)
Set the snow color.

There are also an utility to smoontly cover the object with snow along a determined time.
set the coverTime float to the numbe rof seconds you wish to last the animation and the set startCover bool to true.

Best regards