// This must precede Unity's includes (use HLSLPROGRAM, not CGPROGRAM).
// Runtime enables this keyword only for a single-pass instanced VR camera.
#if defined(CRYSTAL_GOURD_STEREO) && !defined(STEREO_INSTANCING_ON)
    #define STEREO_INSTANCING_ON 1
#endif
#include "UnityCG.cginc"
