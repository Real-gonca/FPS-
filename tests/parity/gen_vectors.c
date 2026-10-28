/*++
    PrecisionCurve - golden vector generator

    Emits the JSON consumed by parity.js (JS port) and by the C# client's
    `--selftest`, so the three implementations of the curve math can be proven
    equivalent.

    This program is the reference: it calls the exact functions the driver's
    LUT is compiled with.
--*/

#include <stdio.h>
#include <string.h>

#define _PRECISIONCURVE_NO_WIN32 1

#include "../../shared/precisioncurve_ioctl.h"
#include "../../shared/precisioncurve_curves.h"

static void emit_params(const PC_CURVE_PARAMS *p)
{
    printf("\"mode\":%d,\"base\":%.17g,\"offset\":%.17g,\"accel\":%.17g,"
           "\"exponent\":%.17g,\"cap\":%.17g,\"max_speed\":%.17g",
           p->Mode, p->Base, p->Offset, p->Accel, p->Exponent, p->Cap,
           p->MaxSpeed);
}

int main(void)
{
    static const double speeds[] = {
        0.0, 0.25, 0.5, 1.0, 2.0, 3.75, 5.0, 10.0, 16.0,
        20.0, 35.5, 60.0, 100.0, 150.0, 199.75, 200.0, 250.0
    };
    const int speedCount = (int)(sizeof(speeds) / sizeof(speeds[0]));
    int first = 1;
    int i;
    int m;
    unsigned long seed = 0x12345678ul;

    printf("{\n  \"generator\": \"tests/parity/gen_vectors.c\",\n");
    printf("  \"abi_version\": %u,\n", PC_ABI_VERSION);

    /* ---- curve evaluation vectors ---- */
    printf("  \"curve_vectors\": [\n");

    for (m = 0; m < PC_CURVE_MODE_COUNT; m++) {
        for (i = 0; i < speedCount; i++) {
            PC_CURVE_PARAMS p = PcPresets[m].Params;

            p.Mode = m;
            if (!first) {
                printf(",\n");
            }
            first = 0;
            printf("    {");
            emit_params(&p);
            printf(",\"speed\":%.17g,\"gain\":%.17g}", speeds[i],
                   PcEvalGain(&p, speeds[i]));
        }
    }

    /* deterministic pseudo-random parameter sets, including awkward ones */
    for (m = 0; m < 40; m++) {
        PC_CURVE_PARAMS p;
        int s;

        seed = seed * 1103515245ul + 12345ul;
        p.Mode = (int)((seed >> 16) % PC_CURVE_MODE_COUNT);
        seed = seed * 1103515245ul + 12345ul;
        p.Base = 0.5 + (double)((seed >> 8) % 400) / 100.0;
        seed = seed * 1103515245ul + 12345ul;
        p.Offset = (double)((seed >> 8) % 2000) / 100.0;
        seed = seed * 1103515245ul + 12345ul;
        p.Accel = (double)((seed >> 8) % 250) / 100.0;
        seed = seed * 1103515245ul + 12345ul;
        p.Exponent = 0.5 + (double)((seed >> 8) % 250) / 100.0;
        seed = seed * 1103515245ul + 12345ul;
        p.Cap = 1.0 + (double)((seed >> 8) % 600) / 100.0;
        seed = seed * 1103515245ul + 12345ul;
        p.MaxSpeed = 10.0 + (double)((seed >> 8) % 1900) / 10.0;

        for (s = 0; s < speedCount; s += 3) {
            if (!first) {
                printf(",\n");
            }
            first = 0;
            printf("    {");
            emit_params(&p);
            printf(",\"speed\":%.17g,\"gain\":%.17g}", speeds[s],
                   PcEvalGain(&p, speeds[s]));
        }
    }
    printf("\n  ],\n");

    /* ---- compiled LUT vectors ---- */
    printf("  \"lut_vectors\": [\n");
    first = 1;
    for (m = 0; m < PC_PRESET_COUNT; m++) {
        static pc_i32 lut[PC_LUT_MAX];
        PC_CURVE_PARAMS p = PcPresets[m].Params;
        pc_u32 maxQ16 = 0;
        pc_u32 n;
        pc_u32 count = (m == 1) ? 64u : 256u;

        p.Mode = m;
        if (PcBuildLut(&p, lut, count, &maxQ16) == 0) {
            return 1;
        }
        if (!first) {
            printf(",\n");
        }
        first = 0;
        printf("    {\"name\":\"%s\",", PcPresets[m].Name);
        emit_params(&p);
        printf(",\"count\":%u,\"max_speed_q16\":%u,\"lut\":[", count, maxQ16);
        for (n = 0; n < count; n++) {
            printf("%s%d", n ? "," : "", lut[n]);
        }
        printf("]}");
    }
    printf("\n  ]\n}\n");
    return 0;
}
