using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

// Reads PRESS:W / RELEASE:W lines from a custom Arduino controller.
// Gameplay should go through GameInput so keyboard, gamepad, and Arduino all work.
[DefaultExecutionOrder(-300)]
public class ArduinoController : MonoBehaviour
{
    public static ArduinoController Instance { get; private set; }

    [Header("Serial settings")]
    [SerializeField] string portName = "COM3";
    [SerializeField] int baudRate = 115200;
    [SerializeField] bool autoFindPort = true;
    [SerializeField] float retrySeconds = 2f;

    WindowsSerialPort port;
    Thread readThread;
    volatile bool running;
    float nextRetryTime;
    bool waitingLogged;

    readonly Queue<string> pendingLines = new Queue<string>();
    readonly object queueLock = new object();

    readonly HashSet<string> heldKeys = new HashSet<string>();
    readonly HashSet<string> pressedThisFrame = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        Ensure();
    }

    public static void Ensure()
    {
        if (Instance != null)
        {
            return;
        }

        ArduinoController existing = FindFirstObjectByType<ArduinoController>();
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        GameObject holder = new GameObject("ArduinoInput");
        holder.AddComponent<ArduinoController>();
    }

    public static Vector2 Move
    {
        get
        {
            float x = 0f;
            float y = 0f;
            if (Held("A"))
            {
                x -= 1f;
            }

            if (Held("D"))
            {
                x += 1f;
            }

            if (Held("S"))
            {
                y -= 1f;
            }

            if (Held("W"))
            {
                y += 1f;
            }

            Vector2 value = new Vector2(x, y);
            if (value.sqrMagnitude > 1f)
            {
                value.Normalize();
            }

            return value;
        }
    }

    public static bool ArmUpHeld => Held("UP") || Held("UPARROW");

    public static bool HeadlightHeld => Held("RIGHT") || Held("RIGHTARROW");

    public static bool HornPressedThisFrame => PressedThisFrame("LEFT") || PressedThisFrame("LEFTARROW");

    public static bool ConfirmPressedThisFrame =>
        PressedThisFrame("ENTER") || PressedThisFrame("RETURN") || PressedThisFrame("NUMPADENTER");

    public static bool Held(string keyName)
    {
        return Instance != null && Instance.IsHeld(keyName);
    }

    public static bool PressedThisFrame(string keyName)
    {
        return Instance != null && Instance.WasPressedThisFrame(keyName);
    }

    public bool IsHeld(string keyName)
    {
        return !string.IsNullOrEmpty(keyName) && heldKeys.Contains(NormalizeKey(keyName));
    }

    public bool WasPressedThisFrame(string keyName)
    {
        return !string.IsNullOrEmpty(keyName) && pressedThisFrame.Contains(NormalizeKey(keyName));
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        TryConnect(true);
    }

    void Update()
    {
        pressedThisFrame.Clear();
        DrainPendingLines();

        if (IsConnected)
        {
            return;
        }

        if (port != null)
        {
            Debug.Log("ArduinoController: disconnected. Waiting to reconnect...");
            Shutdown();
            waitingLogged = false;
        }

        if (Time.unscaledTime >= nextRetryTime)
        {
            TryConnect(false);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        Shutdown();
    }

    void OnApplicationQuit()
    {
        Shutdown();
    }

    bool IsConnected => port != null && port.IsOpen && running;

    void DrainPendingLines()
    {
        List<string> lines = null;
        lock (queueLock)
        {
            if (pendingLines.Count > 0)
            {
                lines = new List<string>(pendingLines);
                pendingLines.Clear();
            }
        }

        if (lines == null)
        {
            return;
        }

        for (int i = 0; i < lines.Count; i++)
        {
            ApplyLine(lines[i]);
        }
    }

    void TryConnect(bool firstAttempt)
    {
        nextRetryTime = Time.unscaledTime + Mathf.Max(0.5f, retrySeconds);
        if (OpenPort())
        {
            waitingLogged = false;
            return;
        }

        if (firstAttempt || !waitingLogged)
        {
            waitingLogged = true;
            Debug.Log("ArduinoController: no Arduino yet. Keyboard and gamepad still work. Will keep checking for a COM port.");
        }
    }

    bool OpenPort()
    {
        List<string> candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(portName))
        {
            candidates.Add(portName.Trim());
        }

        if (autoFindPort)
        {
            try
            {
                string[] available = WindowsSerialPort.GetPortNames();
                for (int i = 0; i < available.Length; i++)
                {
                    string name = available[i];
                    if (!string.IsNullOrWhiteSpace(name) && !candidates.Contains(name))
                    {
                        candidates.Add(name);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("ArduinoController: could not list serial ports. " + e.Message);
            }
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (TryOpen(candidates[i]))
            {
                return true;
            }
        }

        return false;
    }

    bool TryOpen(string name)
    {
        WindowsSerialPort candidate = null;
        try
        {
            candidate = new WindowsSerialPort();
            candidate.Open(name, baudRate);

            port = candidate;
            running = true;
            readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "ArduinoSerial"
            };
            readThread.Start();
            Debug.Log("ArduinoController: opened " + name + " at " + baudRate);
            return true;
        }
        catch (FileNotFoundException)
        {
            if (candidate != null)
            {
                candidate.Dispose();
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Debug.LogWarning("ArduinoController: " + name + " is busy. Close the Arduino Serial Monitor and any other app using that port.");
            if (candidate != null)
            {
                candidate.Dispose();
            }

            return false;
        }
        catch (Exception e)
        {
            Debug.LogWarning("ArduinoController: could not open " + name + ". " + e.Message);
            if (candidate != null)
            {
                candidate.Dispose();
            }

            return false;
        }
    }

    void ReadLoop()
    {
        while (running)
        {
            try
            {
                WindowsSerialPort current = port;
                if (current == null || !current.IsOpen)
                {
                    break;
                }

                string line = current.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                lock (queueLock)
                {
                    pendingLines.Enqueue(line);
                }
            }
            catch (TimeoutException)
            {
            }
            catch (Exception)
            {
                running = false;
            }
        }
    }

    void ApplyLine(string line)
    {
        string[] parts = line.Split(':');
        if (parts.Length != 2)
        {
            return;
        }

        string action = parts[0].Trim().ToUpperInvariant();
        string key = NormalizeKey(parts[1]);
        if (key.Length == 0)
        {
            return;
        }

        if (action == "PRESS")
        {
            if (heldKeys.Add(key))
            {
                pressedThisFrame.Add(key);
            }
        }
        else if (action == "RELEASE")
        {
            heldKeys.Remove(key);
        }
    }

    static string NormalizeKey(string keyName)
    {
        return keyName == null ? string.Empty : keyName.Trim().ToUpperInvariant();
    }

    void Shutdown()
    {
        running = false;
        WindowsSerialPort current = port;
        port = null;
        if (current != null)
        {
            current.Dispose();
        }

        if (readThread != null && readThread.IsAlive)
        {
            readThread.Join(300);
        }

        readThread = null;
    }
}
