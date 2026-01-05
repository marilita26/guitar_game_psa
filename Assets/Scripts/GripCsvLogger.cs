using System.IO;
using UnityEngine;

public class GripCsvLogger : MonoBehaviour
{
    public GripStrengthEstimator est;
    public bool logEnabled = true;

    StreamWriter sw;
    float t0;

    void Start()
    {
        

        if (!est) est = GetComponent<GripStrengthEstimator>();

        t0 = Time.realtimeSinceStartup;

        string path = Path.Combine(Application.persistentDataPath, "grip_log.csv");
        sw = new StreamWriter(path, false);
        sw.WriteLine("t,Bmag,deltaMag,accMag,motion,hasTouch,touchRadius,zMag,zTouch,gripPercent");
        sw.Flush();

        Debug.Log("CSV LOG PATH: " + path);
        Debug.Log("[GripCsvLogger] Start called");

    }

    void Update()
    {
        if (!logEnabled || !est) return;

        float t = Time.realtimeSinceStartup - t0;

        sw.WriteLine(
            $"{t:F4}," +
            $"{est.dbg_Bmag:F6}," +
            $"{est.dbg_deltaMag:F6}," +
            $"{est.dbg_accMag:F6}," +
            $"{est.dbg_motion:F6}," +
            $"{(est.dbg_hasTouch ? 1 : 0)}," +
            $"{est.dbg_touchRadius:F6}," +
            $"{est.dbg_zMag:F6}," +
            $"{est.dbg_zTouch:F6}," +
            $"{est.GripStrengthPercent:F3}"
        );

        if (Time.frameCount % 10 == 0) sw.Flush();
    }

    void OnDestroy()
    {
        if (sw != null) { sw.Flush(); sw.Close(); sw = null; }
    }
}
