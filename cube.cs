using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SG;
using System.IO;
using System;

public class CubeInteraction : MonoBehaviour
{
    public GameObject invisibleSurface;
    public GameObject finishTextObject;
    public GameObject toybox;

    public GameObject boundaryZone;
    public GameObject barrierObject;

    private Vector3 startPosition;
    private bool isProcessing = false;
    private Color originalColor;
    private Renderer CubeRenderer;

    private float startTime;
    private bool timerStarted = false;
    public bool reachedSurface = false;

    private int dropCount = 0;
    private bool wasGrabbed = false;

    private SG_Grabable sgGrabable;

    public Transform thumbTip;
    public Transform indexTip;

    private float initialFingerDistance = 0f;
    private float minFingerDistance = float.MaxValue;

    private string dataFilePath;
    private StreamWriter dataWriter;
    private bool logClosed = false;

    public int participantNumber = 1;
    public string handedness = "left";

    // Feedback sistemi için:
    private List<string> originalFeedbackTypes = new List<string> { "none", "vibration", "force", "combined" };
    private List<string> feedbackTypes = new List<string>();  // Katılımcıya göre karıştırılmış sıra burada olacak
    private Dictionary<string, List<float>> feedbackScaleFactors = new Dictionary<string, List<float>>();

    public int currentFeedbackIndex = 0;
    private int currentScaleIndex = 0;

    private float baseCubeSize = 0.05f;
    public bool experimentFinished = false;

    // Aktif feedback string
    private string FeedBack = "";

    // Feedback script referansları
    private VibrationOnGrab vibrationScript;
    private SG_Material forceScript;

    // ** Position logging için değişkenler **
    private StreamWriter positionLogWriter;
    private string positionLogFilePath;
    private float nextPositionLogTime = 0f;
    public float positionLogInterval = 1f;

    private string previousFeedback = "";
    private string lastLoggedFeedback = "";


    public bool isBreakTime = false;
    public GameObject breakTextObject;

    private float grabStartTime = 0f; // Her scale başında başlar
    private bool canStartTimer = true;  // sadece bir defa süre başlatılsın diye

    public TriangleInteraction triangleScript;
    private bool hasLoggedSurface = false;

    // Interaction & Collision süreleri için
    private float interactionStartTime = 0f;
    private float totalInteractionTime = 0f;

    // contact ile ilgili
    private Collider myCollider;
    public Collider toyboxCollider;

    public float contactThreshold = 0.05f; // ayarla (metre). toybox/cube boyutlarına göre dene
    private bool wasTouchingToybox = false;
    private int contactCount = 0;
    private int currentContactIndex = 0;
    private float contactStartTime = -1f;
    private float totalContactTime = 0f;
    private bool isTouching = false;
    private int activeContacts = 0;
    private float lastDropTime = 0f;   // Son bırakma zamanı
    private float totalContactDuration = 0f;


    private List<float> collisionDurations = new List<float>();

    public static class BreakManager
    {
        public static bool isBreakActive = false;
        public static bool breakCoroutineRunning = false;
    }


    void Start()
    {
        startPosition = transform.position;
        CubeRenderer = GetComponent<Renderer>();
        if (CubeRenderer != null)
        {
            originalColor = CubeRenderer.material.color;
        }

        SetSurfaceVisible(false);
        sgGrabable = GetComponent<SG_Grabable>();

        if (finishTextObject != null)
        {
            finishTextObject.SetActive(false);
        }

        vibrationScript = GetComponent<VibrationOnGrab>();
        forceScript = GetComponent<SG_Material>();

        InitializeDataLog();
        InitializePositionLogger();
        InitializeAllFeedbackScaleFactors();

        // Katılımcı bazlı feedback sırasını karıştır ve ata
        feedbackTypes = GetDeterministicShuffle(originalFeedbackTypes, participantNumber);

        currentFeedbackIndex = 0;
        currentScaleIndex = 0;

        myCollider = GetComponent<Collider>();
        toyboxCollider = toybox != null ? toybox.GetComponent<Collider>() : null;

        if (toybox == null || toyboxCollider == null)
            Debug.LogWarning("toybox veya toybox collider atanmadı!");


        ApplyCurrentFeedbackAndScale();
    }


    // Deterministik shuffle (aynı participantNumber için hep aynı sıra)
    List<T> GetDeterministicShuffle<T>(List<T> list, int seed)
    {
        List<T> newList = new List<T>(list);
        System.Random rng = new System.Random(seed);

        int n = newList.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            T value = newList[k];
            newList[k] = newList[n];
            newList[n] = value;
        }
        return newList;
    }

    void InitializeDataLog()
    {
        try
        {
            string folderPath = System.IO.Path.Combine(Application.persistentDataPath, "InteractionData");
            System.IO.Directory.CreateDirectory(folderPath);

            string fileName = $"Participant_{participantNumber}_Cube_InteractionLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            dataFilePath = System.IO.Path.Combine(folderPath, fileName);

            dataWriter = new StreamWriter(dataFilePath, true);
            dataWriter.WriteLine("Participant;Shape;Hand;FeedBack;EventType;TimeTaken;DropCount;CubeSize;FingerApproachDelta;InteractionTime;TotalContactDuration");
            dataWriter.Flush();

            Debug.Log($"Logging data to: {dataFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize data logging: {e.Message}");
        }
    }

    // ** Yeni: Position Logger Başlatma **
    void InitializePositionLogger()
    {
        try
        {
            string folderPath = System.IO.Path.Combine(Application.persistentDataPath, "Logs", "PositionLogs");
            System.IO.Directory.CreateDirectory(folderPath);

            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"Participant_{participantNumber}_Cube_PositionLog_{timeStamp}.txt";
            positionLogFilePath = System.IO.Path.Combine(folderPath, fileName);

            positionLogWriter = new StreamWriter(positionLogFilePath, true);
            positionLogWriter.WriteLine("Time,Participant,Shape,FeedBack,CubeScale,PosX,PosY,PosZ");
            positionLogWriter.Flush();

            Debug.Log("Cube position logging started: " + positionLogFilePath);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to initialize position logger: " + e.Message);
        }
    }



    void InitializeAllFeedbackScaleFactors()
    {
        System.Random rng = new System.Random();

        foreach (var fb in originalFeedbackTypes)
        {
            List<float> scales = new List<float> { 1f, 1.25f, 1.5f, 1.75f, 2f };

            // E�er istersen scales'i kar��t�rabilirsin:
            int n = scales.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                float temp = scales[k];
                scales[k] = scales[n];
                scales[n] = temp;
            }
            feedbackScaleFactors[fb] = scales;
        }
    }

    public void ApplyCurrentFeedbackAndScale()
    {
        if (experimentFinished || isBreakTime) return;

        if (currentFeedbackIndex >= feedbackTypes.Count)
        {
            FinishExperiment();
            return;
        }

        // *** Interaction ve collision sürelerini sıfırla ***
        totalInteractionTime = 0f;
        //contactCount = 0;
        totalContactDuration = 0f;
        contactStartTime = 0f;
        wasTouchingToybox = false;
        currentContactIndex = 0;


        // Feedback ve scale listesini al
        string currentFeedback = feedbackTypes[currentFeedbackIndex];
        List<float> currentScaleList = feedbackScaleFactors[currentFeedback];

        // Ölçek bitmişse feedback değiştir
        if (currentScaleIndex >= currentScaleList.Count)
        {
            currentFeedbackIndex++;
            currentScaleIndex = 0;

            // Eğer 2. feedback (index 1) tamamlandıysa ve şimdi 3. feedback'e geçiliyorsa -> mola ver
            if (currentFeedbackIndex == 2 && triangleScript.currentFeedbackIndex == 2)
            {
                Debug.Log(">> Starting break after 2 feedbacks");
                StartCoroutine(StartBreakCoroutine());
                return;
            }

            if (currentFeedbackIndex >= feedbackTypes.Count)
            {
                FinishExperiment();
                return;
            }

            currentFeedback = feedbackTypes[currentFeedbackIndex];
            currentScaleList = feedbackScaleFactors[currentFeedback];
        }

        float scaleFactor = currentScaleList[currentScaleIndex];
        //currentScaleIndex++;

        transform.position = startPosition;
        transform.localScale = Vector3.one * (baseCubeSize * scaleFactor);

        if (toybox != null)
        {
            Vector3 baseToyboxScale = new Vector3(1f, 0.3f, 1f);
            toybox.transform.localScale = new Vector3(baseToyboxScale.x * scaleFactor, baseToyboxScale.y, baseToyboxScale.z * scaleFactor);
        }

        FeedBack = currentFeedback;

        UpdateFeedbackScripts();

        Debug.Log($"Applied Feedback: {FeedBack} | Scale factor: {scaleFactor}");

        grabStartTime = 0f;  // yeni boyut geldiğinde sıfırla
        timerStarted = false;
        dropCount = 0;
        canStartTimer = true;  // yeni boyut geldiğinde tekrar grab süresi başlasın
        reachedSurface = false;
        isBreakTime = false;  

        /*triangleScript.reachedSurface = false;

                          // bu break'ten çıkıldığını garanti eder
        triangleScript.isBreakTime = false;*/

    }



    void UpdateFeedbackScripts()
    {
        if (vibrationScript != null)
            vibrationScript.enabled = false;

        if (forceScript != null)
        {
            forceScript.enabled = false;

            if (forceScript.materialProperties != null)
                forceScript.materialProperties.maxForce = 0f;
        }

        switch (FeedBack)
        {
            case "none":
                break;

            case "vibration":
                if (vibrationScript != null)
                    vibrationScript.enabled = true;
                break;

            case "force":
                if (forceScript != null)
                {
                    forceScript.enabled = true;
                    if (forceScript.materialProperties != null)
                        forceScript.materialProperties.maxForce = 1f;
                }
                break;

            case "combined":
                if (vibrationScript != null)
                    vibrationScript.enabled = true;
                if (forceScript != null)
                {
                    forceScript.enabled = true;
                    if (forceScript.materialProperties != null)
                        forceScript.materialProperties.maxForce = 1f;
                }
                break;
        }
    }

    private float ComputeColliderDistance()
    {
        if (myCollider == null || toyboxCollider == null) return float.MaxValue;

        // ClosestPoint'ler ile doğru yüzeyler arası mesafe al
        Vector3 pOnMe = myCollider.ClosestPoint(toybox.transform.position);
        Vector3 pOnToy = toyboxCollider.ClosestPoint(transform.position);
        return Vector3.Distance(pOnMe, pOnToy);
    }


    void Update()
    {
        bool isGrabbed = sgGrabable.IsGrabbed();

        if (isGrabbed && !wasGrabbed)
        {
            OnGrabStart();
        }
        else if (!isGrabbed && wasGrabbed)
        {
            OnGrabEnd();
        }

        if (!logClosed && isGrabbed && thumbTip != null && indexTip != null)
        {
            float currentDistance = Vector3.Distance(thumbTip.position, indexTip.position);
            if (currentDistance < minFingerDistance)
            {
                minFingerDistance = currentDistance;
            }
        }

        if (!isProcessing && timerStarted && !reachedSurface)
        {
            Collider CubeCol = GetComponent<Collider>();
            Collider surfaceCol = invisibleSurface.GetComponent<Collider>();

            if (CubeCol != null && surfaceCol != null && CubeCol.bounds.Intersects(surfaceCol.bounds) && triangleScript.reachedSurface)
            {
                float timeTaken = Time.time - startTime;
                //LogInteraction(timeTaken);
                dropCount = 0;
                timerStarted = false;
                reachedSurface = true;
                StartCoroutine(HandleInteraction());
            }
        }

        wasGrabbed = isGrabbed;

        if (boundaryZone != null && barrierObject != null && thumbTip != null && indexTip != null)
        {
            bool thumbInside = IsInsideBoundaryZone(thumbTip.position);
            bool indexInside = IsInsideBoundaryZone(indexTip.position);

            barrierObject.SetActive(thumbInside || indexInside ? false : true);
        }


        if (positionLogWriter != null && Time.time >= nextPositionLogTime)
        {
            LogPosition();
            nextPositionLogTime = Time.time + positionLogInterval;
        }

        if (triangleScript != null)
        {
            // İkisi de yüzeye ulaştıysa (büyüme için tetikleme)
            if (reachedSurface && triangleScript.reachedSurface && !isBreakTime && !triangleScript.isBreakTime)
            {
                Debug.Log("Both Cube and Triangle reached surface - trigger growth or next step.");
                ApplyCurrentFeedbackAndScale();
                triangleScript.ApplyCurrentFeedbackAndScale();
            }

            // İkisi de breakteyse molayı başlat
            if (reachedSurface && triangleScript.reachedSurface &&
                currentFeedbackIndex == 2 && triangleScript.currentFeedbackIndex == 2 &&
                !BreakManager.isBreakActive)
            {
                StartCoroutine(StartBreakCoroutine());
                StartCoroutine(triangleScript.StartBreakCoroutine());
            }


            // İkisi de deney bittiğinde final mesajı
            if (experimentFinished && triangleScript.experimentFinished)
            {
                Debug.Log("Experiment finished for both Cube and Triangle.");
                if (finishTextObject != null)
                    finishTextObject.SetActive(true);
            }
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            ResetCubePosition();
        }

        /*if (toybox != null)
        {
            float distance = Vector3.Distance(transform.position, toybox.transform.position);
            bool isCurrentlyTouching = distance <= contactThreshold;

            if (!wasTouchingToybox && isCurrentlyTouching)
            {
                // Temas başladı
                contactCount++;
                contactStartTime = Time.time;
                wasTouchingToybox = true;

                Debug.Log($"Temas başladı, temas sayısı: {contactCount}");
            }
            else if (wasTouchingToybox && !isCurrentlyTouching)
            {
                // Temas bitti
                float contactDuration = Time.time - contactStartTime;
                totalContactTime += contactDuration;

                Debug.Log($"Temas bitti, süre: {contactDuration:F2}s, toplam süre: {totalContactTime:F2}s");

                if (dataWriter != null && !logClosed)
                {
                    try
                    {
                        dataWriter.WriteLine($"{participantNumber};Cube;{handedness};{FeedBack};ContactWithToybox;ContactDuration={contactDuration:F2};TotalDuration={totalContactTime:F2};Count={contactCount};;{transform.localScale.x:F4};fingerApproach;InteractionTime={totalInteractionTime:F2}");
                        dataWriter.Flush();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Temas loglama hatası: {e.Message}");
                    }
                }

                wasTouchingToybox = false;
            }
        }*/

        // Her frame'de tüm toybox çocukları ile mesafeyi kontrol et
        /*bool isTouchingAny = false;
        foreach (Transform child in toybox.transform)
        {
            float distance = Vector3.Distance(transform.position, child.position);
            float contactThreshold = 0.05f; // Temas eşiği (ayarlayabilirsiniz)
            if (distance <= contactThreshold)
            {
                isTouchingAny = true;
                break; // En az bir temas var, diğerlerini kontrol etmeye gerek yok
            }
        }

        // Temas başladıysa
        if (isTouchingAny && activeContacts == 0)
        {
            contactCount++;
            contactStartTime = Time.time;
            activeContacts = 1;

            Debug.Log($"Contact START #{contactCount}");
        }
        // Temas bitti ise
        else if (!isTouchingAny && activeContacts > 0)
        {
            float contactDuration = Time.time - contactStartTime;
            totalContactTime += contactDuration;
            activeContacts = 0;

            Debug.Log($"Contact END #{contactCount}, Duration={contactDuration:F3}s, Total={totalContactTime:F3}s");

            if (dataWriter != null && !logClosed)
            {
                try
                {
                    dataWriter.WriteLine($"{participantNumber};Cube;{handedness};{FeedBack};ContactWithToybox;ContactDuration={contactDuration:F3};TotalDuration={totalContactTime:F3};Count={contactCount};;{transform.localScale.x:F4};fingerApproach;InteractionTime=0");
                    dataWriter.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Temas loglama hatası: {e.Message}");
                }
            }
        }*/
    }


    /*    void OnTriggerEnter(Collider other)
        {
            if (toybox != null && other.transform.IsChildOf(toybox.transform))
            {
                activeContacts++;
                if (activeContacts == 1 && !reachedSurface) // ilk temas başladığında zaman kaydet
                {
                    contactStartTime = Time.time;
                    Debug.Log("Contact START with " + other.gameObject.name);
                }
            }
        }

        // Toybox collider çıkışları
        void OnTriggerExit(Collider other)
        {
            if (toybox != null && other.transform.IsChildOf(toybox.transform))
            {
                activeContacts--;
                if (activeContacts == 0 && contactStartTime > 0f && !reachedSurface) // son temas bittiğinde süreyi ekle
                {
                    float duration = Time.time - contactStartTime;
                    totalContactDuration += duration;
                    contactCount++;
                    Debug.Log($"Contact END with {other.gameObject.name}, Duration={duration:F3}s, Total={totalContactDuration:F3}s");

                    contactStartTime = 0f; // reset
                }
            }
        }*/

    void OnTriggerEnter(Collider other)
    {
        if (toybox != null && other.transform.IsChildOf(toybox.transform))
        {
            activeContacts++;

            // İlk temas başladığında sadece bir kez zaman kaydet
            if (activeContacts == 1 && contactStartTime <= 0f)
            {
                contactStartTime = Time.time;
                Debug.Log($"[Contact START] {other.gameObject.name} - t={contactStartTime:F3}");
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (toybox != null && other.transform.IsChildOf(toybox.transform))
        {
            activeContacts = Mathf.Max(0, activeContacts - 1);

            // Son temas bittiğinde süreyi ekle
            if (activeContacts == 0 && contactStartTime > 0f)
            {
                float duration = Time.time - contactStartTime;

                // Mantıksız değerleri engelle
                if (duration > 0.001f && duration < 30f)
                {
                    totalContactDuration += duration;
                    contactCount++;
                    Debug.Log($"[Contact END] {other.gameObject.name} - Δt={duration:F3}s (total={totalContactDuration:F3})");
                }

                contactStartTime = 0f; // Resetle
            }
        }
    }









    public void ResetCubePosition()
    {
        transform.position = startPosition;
        GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        GetComponent<Rigidbody>().angularVelocity = Vector3.zero;
    }

    void FixedUpdate()
    {
        if (!isProcessing && !reachedSurface)
        {
            Collider surfaceCol = invisibleSurface.GetComponent<Collider>();
            Bounds CubeBounds = GetComponent<Collider>().bounds;

            Collider[] hits = Physics.OverlapBox(CubeBounds.center, CubeBounds.extents, Quaternion.identity);

            foreach (Collider col in hits)
            {
                if (col.gameObject == invisibleSurface)
                {
                    float timeTaken = Time.time - startTime;
                    LogInteraction(timeTaken);
                    dropCount = 0;
                    timerStarted = false;
                    reachedSurface = true;

                    StartCoroutine(HandleInteraction());
                    break;
                }
            }
        }

    }

    void OnGrabStart()
    {
        if (canStartTimer)
        {
            grabStartTime = Time.time;
            timerStarted = true;
            canStartTimer = false; // tekrar grab edildiğinde süre başlamasın
        }

        // Interaction time başlat
        interactionStartTime = Time.time;
        reachedSurface = false;
        float pickupTime = 0;

        if (lastDropTime > 0f)
        {
            pickupTime = Time.time - lastDropTime;

            // Log dosyasına yaz
            if (dataWriter != null && !logClosed)
            {
                try
                {
                    dataWriter.WriteLine($"{participantNumber};Cube;{handedness};{FeedBack};PickedUp;PickupTime={pickupTime:F2};{dropCount};;{transform.localScale.x:F4};fingerApproach;{totalInteractionTime:F2};");
                    dataWriter.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to log pickup event: {e.Message}");
                }
            }
        }

        if (thumbTip != null && indexTip != null)
        {
            initialFingerDistance = Vector3.Distance(thumbTip.position, indexTip.position);
            minFingerDistance = initialFingerDistance;
        }

        Debug.Log("Cube grabbed.");
    }



    void OnGrabEnd()
    {
        // Interaction süresi ekle
        totalInteractionTime += Time.time - interactionStartTime;

        if (!reachedSurface)
        {
            dropCount++;
            float dropTime = Time.time - grabStartTime;
            float fingerApproachDelta = initialFingerDistance - minFingerDistance;

            Debug.Log($"Cube dropped. Drop count: {dropCount}");
            lastDropTime = Time.time;

            if (dataWriter != null && !logClosed)
            {
                try
                {
                    dataWriter.WriteLine($"{participantNumber};Cube;{handedness};{FeedBack};Dropped;{dropTime:F2};{dropCount};{transform.localScale.x:F4};{fingerApproachDelta:F4};{totalInteractionTime:F2};");
                    dataWriter.Flush();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to log drop event: {e.Message}");
                }
            }
        }

        timerStarted = false;
    }


    void LogInteraction(float unusedTimeTaken)
    {
        try
        {
            float fingerApproachDelta = initialFingerDistance - minFingerDistance;
            float totalTimeTaken = Time.time - grabStartTime;

            // NaN, Infinity veya çok saçma büyük/negatif değer varsa loglama!
            if (float.IsNaN(fingerApproachDelta) || float.IsInfinity(fingerApproachDelta) || fingerApproachDelta < -1000f || fingerApproachDelta > 100f)
            {
                Debug.LogWarning($"[LOG SKIPPED] Unreasonable fingerApproachDelta: {fingerApproachDelta}");
                return;
            }

            /*if (activeContacts > 0 && contactStartTime > 0f)
            {
                float duration = Time.time - contactStartTime;
                totalContactDuration += duration;
                contactCount++;
                activeContacts = 0;       // aktif teması sıfırla
                contactStartTime = 0f;    // zamanı resetle
                Debug.Log($"Contact finalized at ReachedSurface. Duration={duration:F3}s, Total={totalContactDuration:F3}s");
            }*/
            if (activeContacts > 0 && contactStartTime > 0f)
            {
                // Henüz Exit çağrılmadıysa son kısmı tamamla
                float extraDuration = Time.time - contactStartTime;
                if (extraDuration > 0.001f)
                {
                    totalContactDuration += extraDuration;
                    contactCount++;
                    Debug.Log($"[Contact FINALIZED at Surface] Extra={extraDuration:F3}s, Total={totalContactDuration:F3}s");
                }

                // Resetle ki sonraki denemede doğru çalışsın
                activeContacts = 0;
                contactStartTime = 0f;
            }


            if (dataWriter != null && !logClosed)
            {
                //dataWriter.WriteLine($"{participantNumber};Cube;{handedness};{FeedBack};ReachedSurface;{totalTimeTaken:F2};{dropCount};{transform.localScale.x:F4};{fingerApproachDelta:F4};InteractionTime={totalInteractionTime:F2}");
                dataWriter.WriteLine(
               $"{participantNumber};Cube;{handedness};{FeedBack};ReachedSurface;" +
               $"{totalTimeTaken:F2};{dropCount};{transform.localScale.x:F4};" +
               $"{fingerApproachDelta:F4};{totalInteractionTime:F2};" +
               $"{totalContactDuration:F3}"
                );
                dataWriter.Flush();
                currentScaleIndex++;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to log interaction: {e.Message}");
        }
    }





    void LogPosition()
    {
        if (positionLogWriter == null)
            return;

        try
        {
            Vector3 scale = transform.localScale;
            Vector3 position = transform.position;
            string timeStamp = Time.time.ToString("F2");

            string line = $"{timeStamp};{participantNumber};Cube;{FeedBack};{scale.x:F4};{position.x:F4};{position.y:F4};{position.z:F4}";
            positionLogWriter.WriteLine(line);
            positionLogWriter.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to log position: " + e.Message);
        }
    }

    IEnumerator HandleInteraction()
    {
        isProcessing = true;

        if (CubeRenderer != null)
        {
            CubeRenderer.material.color = Color.magenta;
        }

        SetSurfaceVisible(true);

        yield return new WaitForSeconds(0.1f);

        if (experimentFinished)
            yield break;

        initialFingerDistance = 0f;
        minFingerDistance = float.MaxValue;

        yield return new WaitForSeconds(0.1f);

        if (CubeRenderer != null)
        {
            CubeRenderer.material.color = originalColor;
        }

        SetSurfaceVisible(false);
        isProcessing = false;

        /*if (reachedSurface && triangleScript.reachedSurface && !isBreakTime && !triangleScript.isBreakTime)
        {
            Debug.Log("Both Cube and Triangle reached surface - trigger growth or next step.");
            ApplyCurrentFeedbackAndScale();
            triangleScript.ApplyCurrentFeedbackAndScale();
        }*/


        // Eğer hala temas açıksa (exit gelmeden bitti)
        if (activeContacts > 0 && contactStartTime > 0f)
        {
            float duration = Time.time - contactStartTime;
            totalContactDuration += duration;
            Debug.Log($"[Contact finalized at surface] Duration={duration:F3}s, Total={totalContactDuration:F3}s");
            contactStartTime = 0f;
            activeContacts = 0;
        }


        canStartTimer = true;  // surface'e ulaşıldığında tekrar süre başlatılabilir

    }

    public IEnumerator StartBreakCoroutine()
    {
        if (BreakManager.breakCoroutineRunning)
            yield break;

        BreakManager.breakCoroutineRunning = true;
        BreakManager.isBreakActive = true;

        isBreakTime = true;

        if (breakTextObject != null)
            breakTextObject.SetActive(true);

        // Objeyi sahne dışına gönder
        transform.position = new Vector3(1000, 1000, 1000);

        Debug.Log("Break started (15 seconds)");

        yield return new WaitForSeconds(15f);

        transform.position = startPosition;

        if (breakTextObject != null)
            breakTextObject.SetActive(false);

        isBreakTime = false;

        BreakManager.isBreakActive = false;
        BreakManager.breakCoroutineRunning = false;

        Debug.Log("Break ended");

        ApplyCurrentFeedbackAndScale();
    }





    void FinishExperiment()
    {
        if (experimentFinished) return;

        experimentFinished = true;

        Debug.Log("All feedbacks and scales tested. Experiment finished.");

        if (dataWriter != null && !logClosed)
        {
            dataWriter.WriteLine($"{participantNumber};Cube;{handedness};{FeedBack};AllScalesTested;{dropCount};DONE");
            dataWriter.Flush();
            dataWriter.Close();
            logClosed = true;
        }

        if (positionLogWriter != null)
        {
            try
            {
                positionLogWriter.Flush();
                positionLogWriter.Close();
                Debug.Log("Position log closed.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing position log file: {e.Message}");
            }
        }

        if (finishTextObject != null)
        {
            finishTextObject.SetActive(true);
        }
    }

    void SetSurfaceVisible(bool visible)
    {
        Renderer rend = invisibleSurface.GetComponent<Renderer>();
        if (rend != null)
        {
            Color color = rend.material.color;
            color.a = visible ? 1f : 0f;
            rend.material.color = color;
        }
    }

    bool IsInsideBoundaryZone(Vector3 position)
    {
        Collider zoneCollider = boundaryZone.GetComponent<Collider>();
        if (zoneCollider != null)
            return zoneCollider.bounds.Contains(position);

        return false;
    }

    void OnDestroy()
    {
        if (dataWriter != null && !logClosed)
        {
            try
            {
                dataWriter.Close();
                Debug.Log("Data log closed.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing log file: {e.Message}");
            }
        }

        if (positionLogWriter != null)
        {
            try
            {
                positionLogWriter.Flush();
                positionLogWriter.Close();
                Debug.Log("Position log closed.");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing position log file: {e.Message}");
            }
        }
    }
}