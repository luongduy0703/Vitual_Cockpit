using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System;
using System.Text;

public class ChatManager : MonoBehaviour
{
    [SerializeField] private Communication communication;
    [SerializeField] private Telemetry telemetry;

    [Header("UI References")]
    public TMP_InputField inputField;
    public Button sendButton;
    public Button voiceButton;
    public TextMeshProUGUI statusText;
    public GameObject messagePrefab;
    public Transform messageContainer;
    public ScrollRect chatScrollRect;

    [Header("API Settings")]
    public string apiKey = "your_api_key_here";
    public string apiUrl = "https://api.deepseek.com/v1/chat/completions";

    public enum CommandMode
    {
        LocalThenDeepSeek = 0,
        LocalOnly = 1,
        DeepSeekOnly = 2
    }

    [Header("Command Mode")]
    [SerializeField] private CommandMode commandMode = CommandMode.LocalThenDeepSeek;

    [Header("Drawing Canvas Toggle")]
    [SerializeField] private GameObject simpleDrawingCanvasGO;

    private bool isWaitingForResponse = false;
    private string lastAIResult = "";

    void Start()
    {
        if (communication == null)
        {
            communication = GetComponent<Communication>();
            if (communication == null)
            {
                Debug.LogError("Communication component not found in hierarchy.");
                enabled = false;
                return;
            }
        }

        var communicationObj = GameObject.FindGameObjectWithTag("Communication");
        if (communicationObj == null)
        {
            Debug.LogError("Communication GameObject not found. Please check the tag.");
            enabled = false;
            return;
        }

        var telemetryTransform = communicationObj.transform.Find("Telemetry");
        if (telemetryTransform == null)
        {
            Debug.LogError("Telemetry child not found under Communication GameObject.");
            enabled = false;
            return;
        }

        telemetry = telemetryTransform.GetComponent<Telemetry>();
        if (telemetry == null)
        {
            Debug.LogError("Telemetry component not found on Telemetry GameObject.");
            enabled = false;
            return;
        }

        sendButton.onClick.AddListener(OnSendButtonClick);
        voiceButton.onClick.AddListener(OnVoiceButtonClick);
        inputField.onEndEdit.AddListener(OnInputEndEdit);

        statusText.text = "Sẵn sàng kết nối";
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) && !isWaitingForResponse)
        {
            OnSendButtonClick();
        }
    }

    public void OnSendButtonClick()
    {
        if (string.IsNullOrEmpty(inputField.text) || isWaitingForResponse) return;

        string message = inputField.text;
        inputField.text = "";

        AddMessageToChat(message, true);

        string fullmappingPrompt =
            "Khi nhận được văn bản mô tả, hãy trả về CHÍNH XÁC tên hàm tương ứng theo mapping dưới đây, " +
            "KHÔNG thêm bất kỳ giải thích, bình luận hay ký tự nào khác: " +
            "\r\n\r\n\"kết nối\" -> \"VCConnect\" " +
            "\r\n\r\n\"bay lên\" -> \"VCTakeoff\"  " +
            "\r\n\r\n\"tiến lên\" -> \"VCForward\"" +
            "\r\n\r\n \"lùi xuống\" -> \"VCBackward\" " +
            "\r\n\r\n \"sang trái\" -> \"VCLeft\" " +
            "\r\n\r\n \"sang phải\" -> \"VCRight\" " +
            "\r\n\r\n \"hạ cánh\" -> \"VCLand\" " +
            "\r\n\r\n \"quay phải\" -> \"VCRotateR\" " +
            "\r\n\r\n \"quay trái\" -> \"VCRotateL\" " +
            "\r\n\r\n\"hiện bảng vẽ\" -> \"VCDrawingShow\" " +
            "\r\n\r\n\"ẩn bảng vẽ\" -> \"VCDrawingHide\" " +
            "\r\n\r\nQUY TẮC: " +
            "\r\n\r\n1. Chỉ trả về tên hàm duy nhất, không thêm gì khác " +
            "\r\n2. Áp dụng cho cả các từ/cụm từ có nghĩa tương đương hoặc gần giống " +
            "\r\n3. Nếu không khớp với bất kỳ mô tả nào, trả về chuỗi rỗng \"\" " +
            "\r\n\r\nText:";

        string fullPrompt = fullmappingPrompt + " " + message;
        StartCoroutine(SendToDeepSeek(fullPrompt));
    }

    public void SendVoiceCommand(string recognizedText)
    {
        if (string.IsNullOrEmpty(recognizedText) || isWaitingForResponse) return;

        AddMessageToChat(recognizedText, true);

        switch (commandMode)
        {
            case CommandMode.LocalOnly:
                if (!TryExecuteLocalCommandFromText(recognizedText))
                    AddMessageToChat("No detected command (LocalOnly).", false);
                return;

            case CommandMode.DeepSeekOnly:
                StartCoroutine(SendToDeepSeek(BuildVoiceMappingPrompt(recognizedText)));
                return;

            case CommandMode.LocalThenDeepSeek:
            default:
                if (TryExecuteLocalCommandFromText(recognizedText))
                    return;

                StartCoroutine(SendToDeepSeek(BuildVoiceMappingPrompt(recognizedText)));
                return;
        }
    }

    private bool TryExecuteLocalCommandFromText(string rawText)
    {
        string functionName = TryMapCommandLocal_VietnameseWithDiacritics(rawText);
        if (string.IsNullOrEmpty(functionName))
            return false;

        ExecuteMappedFunction(functionName);
        statusText.text = "Completed command (local): " + functionName;
        return true;
    }

    
    private string TryMapCommandLocal_VietnameseWithDiacritics(string rawText)
    {
        // dipose special character by space, hold Vietnamese characters
        string text = NormalizeVietnameseKeepDiacritics(rawText);
        text = RemoveFillerWordsVietnamese(text);

        // Connect
        if (ContainsAny(text, "kết nối", "kết nối lại", "connect"))
            return "VCConnect";

        // Arm
        if (ContainsAny(text,
                "khởi động", "khởi tạo", "arm",
                "chế độ khởi động", "bật khởi động", "bắt đầu", "mở khóa"))
            return "VCArm";

        // Land
        if (ContainsAny(text, "hạ cánh", "đáp xuống", "land", "hạ xuống"))
            return "VCLand";

        // Indoor mode (Offboard)
        if (ContainsAny(text, "cất cánh", "bay lên", "chế độ bay trong nhà", "bay lên cao một mét"))
            return "VCOffboardMode";

        // Circle Fly mode start
        if (ContainsAny(text, "chế độ bay vòng tròn", "bay vòng tròn", "vòng tròn", "circle", "bắt đầu vòng tròn"))
            return "VCCircleStart";

        // Circle Fly mode stop
        if (ContainsAny(text, "tạm dừng chế độ", "dừng chế độ", "dừng vòng tròn", "tạm dừng vòng tròn", "stop circle", "pause"))
            return "VCCircleStop";

        // Draw canvas enable
        if (ContainsAny(text, "hiện bảng", "mở bảng", "bật bảng", "hiện canvas", "mở canvas", "bật canvas"))
            return "VCDrawingShow";

        // Draw canvas disable
        if (ContainsAny(text, "ẩn bảng", "tắt bảng", "đóng bảng", "ẩn canvas", "tắt canvas", "đóng canvas"))
            return "VCDrawingHide";

        return "";
    }

    private static string NormalizeVietnameseKeepDiacritics(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        string normalized = input.Normalize(NormalizationForm.FormC);

        string lower = normalized.ToLowerInvariant().Trim();

        var sb = new StringBuilder(lower.Length);
        bool prevWasSpace = false;

        foreach (char c in lower)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!prevWasSpace)
                {
                    sb.Append(' ');
                    prevWasSpace = true;
                }
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                prevWasSpace = false;
            }
            else
            {
                // punctuation/symbol -> space
                if (!prevWasSpace)
                {
                    sb.Append(' ');
                    prevWasSpace = true;
                }
            }
        }

        return sb.ToString().Trim();
    }


    private static string RemoveFillerWordsVietnamese(string text)
    {
        string[] fillers =
        {
            "làm ơn", "giúp tôi", "hãy", "nhé", "với", "đi", "được không", "nha"
        };

        for (int i = 0; i < fillers.Length; i++)
            text = text.Replace(fillers[i], " ");

        while (text.Contains("  "))
            text = text.Replace("  ", " ");

        return text.Trim();
    }

    private static bool ContainsAny(string text, params string[] patterns)
    {
        for (int i = 0; i < patterns.Length; i++)
        {
            if (text.Contains(patterns[i]))
                return true;
        }
        return false;
    }

    private string BuildVoiceMappingPrompt(string recognizedText)
    {
        string fullmappingPrompt =
            "Khi nhận được văn bản mô tả, hãy trả về CHÍNH XÁC tên hàm tương ứng theo mapping dưới đây, " +
            "KHÔNG thêm bất kỳ giải thích, bình luận hay ký tự nào khác: " +
            "\r\n\"kết nối\" -> \"VCConnect\" " +
            "\r\n\"chế độ khởi động\" -> \"VCArm\" " +
            "\r\n\"khởi động\" -> \"VCArm\" " +
            "\r\n\"khởi tạo\" -> \"VCArm\" " +
            "\r\n\"chế độ hạ cánh\" -> \"VCLand\" " +
            "\r\n\"hạ cánh\" -> \"VCLand\" " +
            "\r\n\"chế độ bay trong nhà\" -> \"VCOffboardMode\"  " +
            "\r\n\"chế độ bay vòng tròn\" -> \"VCCircleStart\"" +
            "\r\n\"tạm dừng chế độ\" -> \"VCCircleStop\" " +
            "\r\n\"hiện bảng vẽ\" -> \"VCDrawingShow\" " +
            "\r\n\"ẩn bảng vẽ\" -> \"VCDrawingHide\" " +
            "\r\nQUY TẮC: " +
            "\r\n1. Chỉ trả về tên hàm duy nhất, không thêm gì khác " +
            "\r\n2. Áp dụng cho cả các từ/cụm từ có nghĩa tương đương hoặc gần giống " +
            "\r\n3. Nếu không khớp với bất kỳ mô tả nào, trả về chuỗi rỗng \"\" " +
            "\r\nText:";

        return fullmappingPrompt + " " + recognizedText;
    }

    public void OnVoiceButtonClick()
    {
        AddMessageToChat("Voice control send button unavailable!", false);
    }

    void OnInputEndEdit(string text)
    {
        if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter))
        {
            OnSendButtonClick();
        }
    }

    public void AddMessageToChat(string message, bool isUser)
    {
        GameObject newMessage = Instantiate(messagePrefab, messageContainer);

        TextMeshProUGUI messageText = newMessage.GetComponentInChildren<TextMeshProUGUI>();
        if (messageText != null)
        {
            messageText.text = message;

            if (isUser)
            {
                messageText.color = Color.blue;
                messageText.alignment = TextAlignmentOptions.Right;
            }
            else
            {
                messageText.color = Color.white;
                messageText.alignment = TextAlignmentOptions.Left;
            }
        }

        StartCoroutine(ScrollToBottom());
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    IEnumerator SendToDeepSeek(string userMessage)
    {
        isWaitingForResponse = true;
        statusText.text = "Connecting to DeepSeek...";

        DeepSeekMessage[] messageList = new DeepSeekMessage[]
        {
            new DeepSeekMessage { role = "system", content = "Bạn là một trợ lý ảo hữu ích." },
            new DeepSeekMessage { role = "user", content = userMessage }
        };

        DeepSeekRequest requestData = new DeepSeekRequest
        {
            model = "deepseek-chat",
            messages = messageList,
            temperature = 0.7,
            max_tokens = 2000
        };

        string jsonData = JsonUtility.ToJson(requestData);
        byte[] rawData = System.Text.Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(rawData);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            statusText.text = "Sending request ...";
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;

                try
                {
                    DeepSeekResponse response = JsonUtility.FromJson<DeepSeekResponse>(jsonResponse);

                    if (response.choices != null && response.choices.Length > 0)
                    {
                        string aiResponse = response.choices[0].message.content;
                        lastAIResult = aiResponse;

                        AddMessageToChat(aiResponse, false);
                        statusText.text = "Received feedback";

                        ExecuteMappedFunction(aiResponse);
                    }
                    else
                    {
                        AddMessageToChat("Sorry, I don't have feedback for this request", false);
                        statusText.text = "Error: feedback is empty.";
                    }
                }
                catch (Exception e)
                {
                    AddMessageToChat("Error feedback from AI.", false);
                    statusText.text = "Error JSON: " + e.Message;
                }
            }
            else
            {
                AddMessageToChat("Error connect: " + request.error, false);
                statusText.text = "HTTP error: " + request.responseCode;
            }
        }

        isWaitingForResponse = false;
        statusText.text = "Ready";
    }

    private void ExecuteMappedFunction(string functionName)
    {
        switch (functionName)
        {
            case "VCConnect":
                communication.StartUDP();
                break;
            case "VCArm":
                communication.SendCommandArmDisarm((byte)1);
                break;
            case "VCLand":
                communication.SendCommandSetMode(4);
                break;
            case "VCOffboardMode":
                telemetry.SetOffboardMode();
                break;
            case "VCCircleStart":
                telemetry.VCCircleStart();
                break;
            case "VCCircleStop":
                telemetry.VCCircleStop();
                break;

            case "VCDrawingShow":
                if (simpleDrawingCanvasGO != null) simpleDrawingCanvasGO.SetActive(true);
                break;

            case "VCDrawingHide":
                if (simpleDrawingCanvasGO != null) simpleDrawingCanvasGO.SetActive(false);
                break;

            default:
                break;
        }
    }

    [System.Serializable]
    public class DeepSeekRequest
    {
        public string model;
        public DeepSeekMessage[] messages;
        public double temperature;
        public int max_tokens;
    }

    [System.Serializable]
    public class DeepSeekMessage
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    public class DeepSeekResponse
    {
        public string id;
        [SerializeField] public string @object;
        public int created;
        public Choice[] choices;
        public Usage usage;
    }

    [System.Serializable]
    public class Usage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [System.Serializable]
    public class Choice
    {
        public int index;
        public Message message;
        public string finish_reason;
    }

    [System.Serializable]
    public class Message
    {
        public string role;
        public string content;
    }
}