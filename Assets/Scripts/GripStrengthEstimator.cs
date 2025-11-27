using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GripStrengthEstimator : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI strengthText;   // Σύρε εδώ το TextMeshPro από το Canvas

    [Header("Παράθυρο / Baseline")]
    public int sampleRateGuess = 50;       // ~fps
    public float windowSec = 1.0f;         // πόσο "πίσω" κοιτάμε (δευτ)
    public float baselineSec = 3.0f;       // πόσο κρατάει το αρχικό baseline

    [Header("Sensitivity (gain ανά αισθητήρα)")]
    public float magGain = 3.0f;         // πόσο "εύκολα" πιάνει 100% από το μαγνητικό
    public float accGain = 3.0f;         // πόσο εύκολα τιμωρούμε την κίνηση
    public float touchGain = 3.0f;         // πόσο εύκολα ανεβαίνει από το touch area

    [Header("Weights (συνδυασμός αισθητήρων)")]
    [Range(0, 1)] public float wMag = 0.8f;  // πόσο μετράει το μαγνητικό
    [Range(0, 1)] public float wTouch = 0.2f;  // πόσο μετράει το touch
    [Range(0, 1)] public float wMotionPenalty = 0.2f; // πόσο δυνατά τιμωρούμε την κίνηση

    [Header("Shaping")]
    [Range(0, 0.5f)] public float deadZone = 0.2f;   // αγνοεί μικρές αλλαγές
    [Range(0, 1)] public float smoothAlpha = 0.15f;  // 0.1–0.3 πόσο γρήγορα αλλάζει

    // Κυλιόμενα παράθυρα
    Queue<float> qMag = new Queue<float>(); // |B|
    Queue<float> qAcc = new Queue<float>(); // |acc|
    Queue<float> qTouch = new Queue<float>(); // touch radius

    // Baseline (ηρεμία)
    float baselineStdMag = 0f;
    float baselineStdAcc = 0f;
    float baselineTouch = 0f;
    bool baselineDone = false;
    float t0;

    public float GripStrengthPercent { get; private set; }

    int N => Mathf.Max(4, Mathf.FloorToInt(sampleRateGuess * windowSec));

    void Start()
    {
        Input.compass.enabled = true;
        Input.location.Start();   // βοηθάει να "ξυπνήσει" το magnetometer

        t0 = Time.realtimeSinceStartup;

        Debug.Log("[GripMulti] Ready. Δώσε άδεια Τοποθεσίας στο app για να δουλέψει το magnetometer.");
    }

    void Update()
    {
        // Αν ΔΕΝ ακουμπάω την οθόνη → δεν υπάρχει grip
        if (Input.touchCount == 0)
        {
            GripStrengthPercent = Mathf.Lerp(GripStrengthPercent, 0f, smoothAlpha);
            if (strengthText) strengthText.text = "Grip: 0% (no touch)";
            return;
        }

        // 1) Μαγνητικό πεδίο
        Vector3 rawB = Input.compass.rawVector;
        float magB = rawB.magnitude;

        if (float.IsNaN(magB) || magB <= 0f)
        {
            if (strengthText) strengthText.text = "Grip: --% (no mag)";
            return;
        }

        // 2) Accelerometer (μέτρο επιτάχυνσης)
        Vector3 acc = Input.acceleration;
        float accMag = acc.magnitude;

        // 3) Touch area (proxy πίεσης)
        float touchRadius = 0f;
        {
            float sumR = 0f;
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                sumR += touch.radius;
            }
            touchRadius = sumR / Input.touchCount;
        }

        // 4) Γέμισε τα παράθυρα
        qMag.Enqueue(magB);
        if (qMag.Count > N) qMag.Dequeue();

        qAcc.Enqueue(accMag);
        if (qAcc.Count > N) qAcc.Dequeue();

        qTouch.Enqueue(touchRadius);
        if (qTouch.Count > N) qTouch.Dequeue();

        if (qMag.Count < N || qAcc.Count < N || qTouch.Count < N)
        {
            if (strengthText) strengthText.text = "Grip: --% (warming up)";
            return;
        }

        // 5) Στατιστικά παραθύρου
        float meanB, stdB;
        WindowStats(qMag, out meanB, out stdB);

        float meanAcc, stdAcc;
        WindowStats(qAcc, out meanAcc, out stdAcc);

        float meanTouch, stdTouch;
        WindowStats(qTouch, out meanTouch, out stdTouch);

        float t = Time.realtimeSinceStartup - t0;

        // 6) Baseline ηρεμίας
        if (!baselineDone)
        {
            if (t < baselineSec)
            {
                baselineStdMag = Mathf.Lerp(baselineStdMag, stdB, 0.05f);
                baselineStdAcc = Mathf.Lerp(baselineStdAcc, stdAcc, 0.05f);
                baselineTouch = Mathf.Lerp(baselineTouch, meanTouch, 0.05f);

                if (strengthText) strengthText.text = "Grip: --% (calibrating)";
                return;
            }
            else
            {
                baselineDone = true;
                if (baselineStdMag < 1e-6f) baselineStdMag = 1e-3f;
                if (baselineStdAcc < 1e-6f) baselineStdAcc = 1e-3f;
                Debug.Log($"[GripMulti] Baseline stdMag={baselineStdMag:F6}, stdAcc={baselineStdAcc:F6}, touch={baselineTouch:F3}");
            }
        }

        // 7) "Excess" για κάθε αισθητήρα

        // MAG – όσο πιο πολύ αλλάζει το πεδίο με την πίεση
        float excessMag = Mathf.Max(0f, stdB - baselineStdMag);
        float denomMag = baselineStdMag * magGain + 1e-6f;
        float zMag = Mathf.Clamp01(excessMag / denomMag);

        // ACC – ΘΕΛΟΥΜΕ ΝΑ ΜΑΣ ΤΙΜΩΡΕΙ ΟΤΑΝ ΥΠΑΡΧΕΙ ΠΟΛΛΗ ΚΙΝΗΣΗ
        float excessAcc = Mathf.Max(0f, stdAcc - baselineStdAcc);
        float denomAcc = baselineStdAcc * accGain + 1e-6f;
        float zMotion = Mathf.Clamp01(excessAcc / denomAcc); // 0 = ήρεμο, 1 = πολύ κούνημα

        // TOUCH – πόσο μεγαλώνει η επιφάνεια επαφής
        float excessTouch = Mathf.Max(0f, meanTouch - baselineTouch);
        float denomTouch = (baselineTouch + 1f) * touchGain + 1e-6f;
        float zTouch = Mathf.Clamp01(excessTouch / denomTouch);

        // 8) Συνδυασμός: σήμα πίεσης * ποινή κίνησης
        float zGripCore = wMag * zMag + wTouch * zTouch;
        zGripCore = Mathf.Clamp01(zGripCore);

        // Ποινή κίνησης: όσο πιο πολύ κουνάς, τόσο μικρότερο factor
        float motionFactor = 1f - wMotionPenalty * zMotion;   // από 1.0 μέχρι 1-wMotionPenalty
        motionFactor = Mathf.Clamp01(motionFactor);

        float z = zGripCore * motionFactor;

        // 9) Dead-zone
        if (z < deadZone)
            z = 0f;
        else
            z = (z - deadZone) / (1f - deadZone);

        z = Mathf.Clamp01(z);

        // 10) Smoothing
        float target = 100f * z;
        GripStrengthPercent = Mathf.Lerp(GripStrengthPercent, target, smoothAlpha);

        if (strengthText)
            strengthText.text = $"Grip: {GripStrengthPercent:F0}%";
    }

    // ========== HELPERS ==========

    static void WindowStats(Queue<float> q, out float mean, out float std)
    {
        int n = q.Count;
        float sum = 0f;

        foreach (var v in q) sum += v;
        mean = sum / n;

        float var = 0f;
        foreach (var v in q)
        {
            float d = v - mean;
            var += d * d;
        }
        var /= n;
        std = Mathf.Sqrt(var);
    }
}
