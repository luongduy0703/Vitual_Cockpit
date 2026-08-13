using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SpeechRecognizerPlugin;

public class SpeechRecognizer : MonoBehaviour, ISpeechRecognizerPlugin
{
    [SerializeField] private Button startListeningBtn = null;
    [SerializeField] private Button stopListeningBtn = null;
    [SerializeField] private Toggle continuousListeningTgle = null;
    [SerializeField] private TMP_Dropdown languageDropdown = null;

    [SerializeField] private TMP_InputField maxResultsInputField = null;

    [SerializeField] private TextMeshProUGUI resultsTxt = null;
    [SerializeField] private TextMeshProUGUI errorsTxt = null;

    [SerializeField] private ChatManager chatManager;

    private SpeechRecognizerPlugin plugin = null;
    private string lastRecognizedText = "";

    private bool isActiveMode = false;
    private float timeoutSeconds = 2f;
    private Coroutine timeoutCoroutine = null;

    private void Start()
    {
        plugin = SpeechRecognizerPlugin.GetPlatformPluginVersion(this.gameObject.name);

        if (startListeningBtn != null) startListeningBtn.onClick.AddListener(StartListening);
        if (stopListeningBtn != null) stopListeningBtn.onClick.AddListener(StopListening);
        if (continuousListeningTgle != null) continuousListeningTgle.onValueChanged.AddListener(SetContinuousListening);
        if (languageDropdown != null) languageDropdown.onValueChanged.AddListener(SetLanguage);

        plugin.SetContinuousListening(true);
        plugin.SetMaxResultsForNextRecognition(1);
        plugin.StartListening();

        if (maxResultsInputField != null)
        {
            maxResultsInputField.text = "1";
            maxResultsInputField.interactable = false;
        }
    }

    private void StartListening() => plugin.StartListening();
    private void StopListening() => plugin.StopListening();
    private void SetContinuousListening(bool isContinuous) => plugin.SetContinuousListening(isContinuous);

    private void SetLanguage(int dropdownValue)
    {
        if (languageDropdown == null) return;

        string newLanguage = languageDropdown.options[dropdownValue].text;
        plugin.SetLanguageForNextRecognition(newLanguage);
        plugin.SetMaxResultsForNextRecognition(1);
    }

    private void SetMaxResults(string _)
    {
        plugin.SetMaxResultsForNextRecognition(1);
        if (maxResultsInputField != null) maxResultsInputField.text = "1";
    }

    public void OnResult(string recognizedResult)
    {
        if (string.IsNullOrEmpty(recognizedResult))
            return;

        int tildeIndex = recognizedResult.IndexOf('~');
        string best = tildeIndex >= 0 ? recognizedResult.Substring(0, tildeIndex) : recognizedResult;
        best = best.Trim();

        if (string.IsNullOrEmpty(best))
            return;

        isActiveMode = true;

        if (resultsTxt != null)
            resultsTxt.text = best;

        lastRecognizedText = best;
        Debug.Log("<b>RECOGNIZED: </b>" + lastRecognizedText);

        if (chatManager != null)
        {
            chatManager.SendVoiceCommand(lastRecognizedText);
        }

        if (timeoutCoroutine != null)
            StopCoroutine(timeoutCoroutine);
        timeoutCoroutine = StartCoroutine(TimeoutToBackground());
    }

    public void OnVoiceRecognized(string text)
    {
        // Intentionally empty or custom logic.
        // chatManager.SendVoiceCommand(text) đã xử lý hiển thị + parse local + fallback DeepSeek.
    }

    private IEnumerator TimeoutToBackground()
    {
        yield return new WaitForSeconds(timeoutSeconds);
        isActiveMode = false;
        if (resultsTxt != null)
            resultsTxt.text = "Background listening...";
    }

    public void OnError(string recognizedError)
    {
        ERROR error = (ERROR)int.Parse(recognizedError);
        switch (error)
        {
            case ERROR.UNKNOWN:
                Debug.Log("<b>ERROR: </b> Unknown");
                if (errorsTxt != null) errorsTxt.text += "Unknown";
                break;
            case ERROR.INVALID_LANGUAGE_FORMAT:
                Debug.Log("<b>ERROR: </b> Language format is not valid");
                if (errorsTxt != null) errorsTxt.text += "Language format is not valid";
                break;
            default:
                break;
        }
    }
}