#define MA_NO_DEVICE_IO
#define MA_NO_ENCODING
#define MA_NO_ENGINE
#define MA_NO_NODE_GRAPH
#define MA_NO_RESOURCE_MANAGER
#define MA_NO_GENERATION
#define MINIAUDIO_IMPLEMENTATION
#include MA_HEADER
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#define WINDOW 4096
#define MAX_LAG 3000
int main(int argc, char** argv) {
    ma_dr_mp3 mp3;
    if (!ma_dr_mp3_init_file(&mp3, argv[1], NULL)) { printf("open failed\n"); return 2; }
    int ch = mp3.channels;
    ma_uint64 total = ma_dr_mp3_get_pcm_frame_count(&mp3);
    float* ref = malloc(sizeof(float) * ch * (total + 8192));
    ma_dr_mp3_seek_to_pcm_frame(&mp3, 0);
    ma_uint64 got = ma_dr_mp3_read_pcm_frames_f32(&mp3, total + 8192, ref);
    if (argc > 2) ma_dr_mp3_seek_to_pcm_frame(&mp3, 0);
    ma_uint32 count = 256;
    ma_dr_mp3_seek_point* points = malloc(sizeof(*points) * count);
    if (!ma_dr_mp3_calculate_seek_points(&mp3, &count, points) || !ma_dr_mp3_bind_seek_table(&mp3, count, points)) { printf("seek table failed\n"); return 2; }
    float* buf = malloc(sizeof(float) * ch * WINDOW);
    int seeks = 0, exact = 0, lagged = 0; double worstHead = 0; long worstLag = 0; srand(1); long lags[8]={0}; int lagN[8]={0}, distinct=0;
    for (int s = 0; s < 400; s++) {
        ma_uint64 target = s < 40 ? (ma_uint64)s * 97 + 1 : ((ma_uint64)rand() * 4099u) % (got - 2 * WINDOW);
        if (!ma_dr_mp3_seek_to_pcm_frame(&mp3, target)) { printf("seek failed at %llu\n", (unsigned long long)target); continue; }
        ma_uint64 n = ma_dr_mp3_read_pcm_frames_f32(&mp3, WINDOW, buf);
        if (n < WINDOW) continue;
        seeks++;
        double err0 = 0;
        for (int i = 0; i < WINDOW * ch; i++) { double d = fabs(buf[i] - ref[target * ch + i]); if (d > err0) err0 = d; }
        if (err0 == 0) { exact++; continue; }
        long best = 0; double bestErr = 1e30;
        for (long lag = -MAX_LAG; lag <= MAX_LAG; lag++) {
            long start = (long)target + lag; if (start < 0 || start + WINDOW >= (long)got) continue;
            double e = 0; /* compare past the warm-up so a transient can't hide the lag */
            for (int i = 2048 * ch; i < WINDOW * ch && e < bestErr; i++) e += fabs(buf[i] - ref[start * ch + i]);
            if (e < bestErr) { bestErr = e; best = lag; }
        }
        if (best != 0) { lagged++; {int k; for(k=0;k<distinct;k++) if(lags[k]==best){lagN[k]++;break;} if(k==distinct&&distinct<8){lags[distinct]=best;lagN[distinct++]=1;}} if (labs(best) > labs(worstLag)) worstLag = best; }
        else if (err0 > worstHead) worstHead = err0;
    }
    printf("%-34s rate=%-5u delay=%-5u seeks=%-3d exact=%-3d lagged=%-3d worstLag=%-6ld worstMismatchAtLag0=%.5f\n",
           strrchr(argv[1], '/') + 1, mp3.sampleRate, mp3.delayInPCMFrames, seeks, exact, lagged, worstLag, worstHead);
    for(int k=0;k<distinct;k++) printf("    lag %ld x%d\n",lags[k],lagN[k]);
    return lagged != 0 || exact != seeks;
}
