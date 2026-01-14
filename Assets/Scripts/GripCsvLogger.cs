using System.IO;
using System.Globalization;
using UnityEngine;

public class GripCsvLogger : MonoBehaviour
{
    public GripStrengthEstimator est;
    public bool logEnabled = true;

    [Header("Logging")]
    [Tooltip("Flush every N frames (1 = flush every frame).")]
    public int flushEveryFrames = 10;

    StreamWriter sw;
    float t0;

    // Force decimal DOT regardless of phone locale
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    void Start()
    {
        if (!est) est = GetComponent<GripStrengthEstimator>();

        t0 = Time.realtimeSinceStartup;

        string path = Path.Combine(Application.persistentDataPath, "grip_log.csv");
        sw = new StreamWriter(path, false);

        sw.WriteLine("t,Bmag,deltaMag,accMag,motion,hasTouch,touchRadius,zMag,zTouch,gripPercent");
        sw.Flush();

        Debug.Log("CSV LOG PATH: " + path);
    }

    void Update()
    {
        if (!logEnabled || est == null || sw == null) return;

        float t = Time.realtimeSinceStartup - t0;

        string line =
            t.ToString("F4", CI) + "," +
            est.dbg_Bmag.ToString("F6", CI) + "," +
            est.dbg_deltaMag.ToString("F6", CI) + "," +
            est.dbg_accMag.ToString("F6", CI) + "," +
            est.dbg_motion.ToString("F6", CI) + "," +
            (est.dbg_hasTouch ? "1" : "0") + "," +
            est.dbg_touchRadius.ToString("F6", CI) + "," +
            est.dbg_zMag.ToString("F6", CI) + "," +
            est.dbg_zTouch.ToString("F6", CI) + "," +
            est.GripStrengthPercent.ToString("F3", CI);

        sw.WriteLine(line);

        if (flushEveryFrames <= 1 || Time.frameCount % flushEveryFrames == 0)
            sw.Flush();
    }

    void OnApplicationPause(bool pause)
    {
        if (pause) FlushSafe();
    }

    void OnApplicationQuit()
    {
        CloseSafe();
    }

    void OnDestroy()
    {
        CloseSafe();
    }

    void FlushSafe()
    {
        if (sw == null) return;
        try { sw.Flush(); } catch { }
    }

    void CloseSafe()
    {
        if (sw == null) return;
        try
        {
            sw.Flush();
            sw.Close();
        }
        catch { }
        sw = null;
    }
}
