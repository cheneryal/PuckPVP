using UnityEngine;
using UnityEngine.UI;

public class UIHealthBar : MonoBehaviour
{
    private Image fillImage;
    private Canvas canvas;

    void Awake()
    {
        CreateHealthBarUI();
        Hide();
    }

    private void CreateHealthBarUI()
    {
        // Create a new GameObject for the Canvas
        GameObject canvasGo = new GameObject("UIHealthBarCanvas");
        canvasGo.transform.SetParent(transform);
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();
        
        // Background
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(canvasGo.transform, false);
        Image bgImg = bgObj.AddComponent<Image>();
        bgImg.sprite = CTP_AssetLoader.TextureToSprite(CTP_AssetLoader.LoadTexture("hp_bg.png", Color.gray));
        bgImg.rectTransform.sizeDelta = new Vector2(200, 35);
        
        // Anchor to bottom-left
        bgImg.rectTransform.anchorMin = new Vector2(0, 0);
        bgImg.rectTransform.anchorMax = new Vector2(0, 0);
        bgImg.rectTransform.pivot = new Vector2(0, 0);

        // Position above stamina/speed
        bgImg.rectTransform.anchoredPosition = new Vector2(50, 90);

        // Fill
        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(bgObj.transform, false);
        fillImage = fillObj.AddComponent<Image>();
        fillImage.sprite = CTP_AssetLoader.TextureToSprite(CTP_AssetLoader.LoadTexture("hp_fill.png", Color.green));
        
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.rectTransform.sizeDelta = new Vector2(200, 35);
        fillImage.color = Color.white;
    }

    public void SetHealth(float currentHP, float maxHP)
    {
        if (fillImage != null)
        {
            float rawPercent = Mathf.Clamp01(currentHP / maxHP);
            float steps = 7.0f;
            float discreteFill = Mathf.Ceil(rawPercent * steps) / steps;
            fillImage.fillAmount = discreteFill;
        }
    }

    public void Show()
    {
        if (canvas != null) canvas.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (canvas != null) canvas.gameObject.SetActive(false);
    }
}
