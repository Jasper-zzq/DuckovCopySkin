using UnityEngine;
using System.Collections.Generic;
using Duckov.UI;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace BecomeTheBoss
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        // ========== NPC模型替换系统相关 ==========
        private CharacterMainControl playerCharacter;
        private CharacterModel currentNPCModel;
        private Dictionary<string, CharacterModel> availableNPCModels = new Dictionary<string, CharacterModel>();
        private List<string> npcModelNames = new List<string>();
        private bool npcModelSystemInitialized = false;
        private Teams originalTeam = Teams.player; // 保存原始队伍
        
        // UI相关
        private GameObject modelSelectionUI;
        private GameObject modelListPanel;
        private bool isUIVisible = false;
        
        private void Awake()
        {
            Debug.Log("[NPC模型替换] Mod已加载");
            Debug.Log("[NPC模型替换] 按 * (星号键) 打开模型选择菜单");
            
            // 监听场景加载事件
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // 初始化NPC模型替换系统
            StartCoroutine(InitializeNPCModelSystem());
        }
        
        /// <summary>
        /// 场景加载时的回调
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[NPC模型替换] ===== 场景已加载: {scene.name}, 模式: {mode} =====");
            
            // 清理之前场景的NPC模型
            if (currentNPCModel != null)
            {
                Destroy(currentNPCModel.gameObject);
                currentNPCModel = null;
                Debug.Log("[NPC模型替换] 已清理旧场景的NPC模型");
            }
            
            // 重新初始化NPC模型系统
            npcModelSystemInitialized = false;
            StartCoroutine(InitializeNPCModelSystem());
        }
        
        /// <summary>
        /// 初始化NPC模型替换系统
        /// </summary>
        private IEnumerator InitializeNPCModelSystem()
        {
            Debug.Log("[NPC模型替换] 开始初始化...");
            
            // 等待关卡初始化
            while (!LevelManager.LevelInited)
            {
                yield return new WaitForSeconds(0.5f);
            }
            
            // 额外等待，确保所有角色已生成（增加等待时间）
            yield return new WaitForSeconds(3f);
            
            // 获取玩家角色
            playerCharacter = CharacterMainControl.Main;
            
            if (playerCharacter == null)
            {
                Debug.LogWarning("[NPC模型替换] 无法获取玩家角色");
                yield break;
            }
            
            // 保存原始队伍
            originalTeam = playerCharacter.Team;
            Debug.Log($"[NPC模型替换] 原始玩家队伍: {originalTeam}");
            
            // 扫描场景中的NPC和敌人模型
            ScanAvailableNPCModels();
            
            npcModelSystemInitialized = true;
            Debug.Log("[NPC模型替换] 初始化完成");
        }
        
        /// <summary>
        /// 扫描场景中所有可用的NPC模型
        /// </summary>
        private void ScanAvailableNPCModels()
        {
            availableNPCModels.Clear();
            npcModelNames.Clear();
            
            // 方法1：从场景中查找所有CharacterModel（包括非激活的）
            CharacterModel[] allModels = FindObjectsOfType<CharacterModel>(true);
            
            Debug.Log($"[NPC模型替换] 场景中共找到 {allModels.Length} 个CharacterModel");
            
            foreach (var model in allModels)
            {
                // 排除玩家自己的模型
                if (model.characterMainControl == playerCharacter)
                {
                    Debug.Log($"[NPC模型替换] 跳过玩家模型: {model.name}");
                    continue;
                }
                
                // 尝试获取模型名称
                string modelName = model.gameObject.name;
                
                // 如果有关联的CharacterMainControl，添加队伍信息
                if (model.characterMainControl != null)
                {
                    modelName = $"{modelName} ({model.characterMainControl.Team})";
                }
                
                // 避免重复
                if (!availableNPCModels.ContainsKey(modelName))
                {
                    availableNPCModels[modelName] = model;
                    npcModelNames.Add(modelName);
                    Debug.Log($"[NPC模型替换] 发现场景模型: {modelName}");
                }
            }
            
            // 方法2：从CharacterRandomPreset资源中扫描
            ScanPresetsFromResources();
            
            // 方法3：扫描所有CharacterModel资源（包括预制体）
            ScanAllCharacterModelAssets();
            
            Debug.Log($"[NPC模型替换] ===== 共发现 {availableNPCModels.Count} 个可用模型 =====");
            
            if (availableNPCModels.Count > 0)
            {
                Debug.Log("[NPC模型替换] 完整模型列表:");
                for (int i = 0; i < npcModelNames.Count; i++)
                {
                    Debug.Log($"  [{i + 1}] {npcModelNames[i]}");
                }
            }
            else
            {
                Debug.LogWarning("[NPC模型替换] 未找到任何可用模型！");
            }
        }
        
        /// <summary>
        /// 从Resources中扫描CharacterRandomPreset
        /// </summary>
        private void ScanPresetsFromResources()
        {
            try
            {
                CharacterRandomPreset[] presets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                
                Debug.Log($"[NPC模型替换] 找到 {presets.Length} 个CharacterRandomPreset");
                
                foreach (var preset in presets)
                {
                    // 使用反射获取characterModel字段
                    var modelField = typeof(CharacterRandomPreset).GetField("characterModel", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (modelField != null)
                    {
                        CharacterModel model = modelField.GetValue(preset) as CharacterModel;
                        if (model != null)
                        {
                            string presetName = $"[预设] {preset.nameKey ?? preset.name}";
                            
                            if (!availableNPCModels.ContainsKey(presetName))
                            {
                                availableNPCModels[presetName] = model;
                                npcModelNames.Add(presetName);
                                Debug.Log($"[NPC模型替换] 从预设发现模型: {presetName}");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NPC模型替换] 扫描预设时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 方法3：扫描所有已加载的CharacterModel资源（包括预制体）
        /// </summary>
        private void ScanAllCharacterModelAssets()
        {
            try
            {
                // 查找所有CharacterModel资源（包括预制体和未实例化的）
                CharacterModel[] allModelAssets = Resources.FindObjectsOfTypeAll<CharacterModel>();
                
                Debug.Log($"[NPC模型替换] 总共找到 {allModelAssets.Length} 个CharacterModel资源");
                
                int prefabCount = 0;
                
                foreach (var model in allModelAssets)
                {
                    // 排除玩家模型
                    if (model.characterMainControl == playerCharacter)
                        continue;
                    
                    // 判断是否是预制体（不在活动场景中）
                    bool isPrefab = !model.gameObject.scene.IsValid() || string.IsNullOrEmpty(model.gameObject.scene.name);
                    
                    // 优先收集预制体（预制体通常更稳定）
                    if (isPrefab)
                    {
                        string modelName = $"[资源] {model.name}";
                        
                        // 避免重复
                        if (!availableNPCModels.ContainsKey(modelName))
                        {
                            availableNPCModels[modelName] = model;
                            npcModelNames.Add(modelName);
                            prefabCount++;
                            Debug.Log($"[NPC模型替换] 发现预制体模型: {modelName}");
                        }
                    }
                }
                
                Debug.Log($"[NPC模型替换] 预制体扫描完成，新增 {prefabCount} 个模型");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[NPC模型替换] 扫描资源时出错: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 创建模型选择UI
        /// </summary>
        private void CreateModelSelectionUI()
        {
            if (modelSelectionUI != null)
                return;
            
            // 创建Canvas
            modelSelectionUI = new GameObject("ModelSelectionUI");
            Canvas canvas = modelSelectionUI.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // 确保在最上层
            
            CanvasScaler scaler = modelSelectionUI.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            modelSelectionUI.AddComponent<GraphicRaycaster>();
            
            // 创建背景遮罩
            GameObject background = new GameObject("Background");
            background.transform.SetParent(modelSelectionUI.transform, false);
            RectTransform bgRect = background.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            
            Image bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.7f);
            
            // 点击背景关闭UI
            Button bgButton = background.AddComponent<Button>();
            bgButton.onClick.AddListener(HideModelSelectionUI);
            
            // 创建主面板 - 限制最大高度为屏幕的80%
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(modelSelectionUI.transform, false);
            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            
            // 计算面板高度，最大为屏幕高度的80%
            float maxHeight = Screen.height * 0.8f;
            float panelHeight = Mathf.Min(800f, maxHeight);
            panelRect.sizeDelta = new Vector2(600, panelHeight);
            panelRect.anchoredPosition = Vector2.zero;
            
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            
            // 创建标题
            GameObject title = new GameObject("Title");
            title.transform.SetParent(panel.transform, false);
            RectTransform titleRect = title.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.sizeDelta = new Vector2(-40, 80);
            titleRect.anchoredPosition = new Vector2(0, -40);
            
            TextMeshProUGUI titleText = title.AddComponent<TextMeshProUGUI>();
            titleText.text = "选择NPC模型";
            titleText.fontSize = 36;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;
            
            // 创建关闭按钮
            GameObject closeButton = new GameObject("CloseButton");
            closeButton.transform.SetParent(panel.transform, false);
            RectTransform closeRect = closeButton.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1, 1);
            closeRect.anchorMax = new Vector2(1, 1);
            closeRect.sizeDelta = new Vector2(60, 60);
            closeRect.anchoredPosition = new Vector2(-10, -10);
            
            Image closeImage = closeButton.AddComponent<Image>();
            closeImage.color = new Color(0.8f, 0.2f, 0.2f, 1f);
            
            Button closeBtnComponent = closeButton.AddComponent<Button>();
            closeBtnComponent.onClick.AddListener(HideModelSelectionUI);
            
            GameObject closeText = new GameObject("Text");
            closeText.transform.SetParent(closeButton.transform, false);
            RectTransform closeTextRect = closeText.AddComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;
            
            TextMeshProUGUI closeTextTMP = closeText.AddComponent<TextMeshProUGUI>();
            closeTextTMP.text = "×";
            closeTextTMP.fontSize = 40;
            closeTextTMP.alignment = TextAlignmentOptions.Center;
            closeTextTMP.color = Color.white;
            
            // 创建滚动区域
            GameObject scrollView = new GameObject("ScrollView");
            scrollView.transform.SetParent(panel.transform, false);
            RectTransform scrollRect = scrollView.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0, 0);
            scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.offsetMin = new Vector2(20, 20);
            scrollRect.offsetMax = new Vector2(-20, -100);
            
            Image scrollImage = scrollView.AddComponent<Image>();
            scrollImage.color = new Color(0.15f, 0.15f, 0.15f, 1f);
            
            // 添加Mask组件确保内容被裁剪
            Mask scrollMask = scrollView.AddComponent<Mask>();
            scrollMask.showMaskGraphic = true;
            
            ScrollRect scrollRectComponent = scrollView.AddComponent<ScrollRect>();
            scrollRectComponent.horizontal = false;
            scrollRectComponent.vertical = true;
            scrollRectComponent.viewport = scrollRect; // 设置viewport
            
            // 创建内容容器
            GameObject content = new GameObject("Content");
            content.transform.SetParent(scrollView.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 0);
            contentRect.anchoredPosition = Vector2.zero;
            
            VerticalLayoutGroup layoutGroup = content.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 10;
            layoutGroup.padding = new RectOffset(10, 10, 10, 10);
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
            
            ContentSizeFitter sizeFitter = content.AddComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            scrollRectComponent.content = contentRect;
            modelListPanel = content;
            
            // 默认隐藏
            modelSelectionUI.SetActive(false);
            
            Debug.Log("[NPC模型替换] UI已创建");
        }
        
        /// <summary>
        /// 显示模型选择UI
        /// </summary>
        private void ShowModelSelectionUI()
        {
            if (modelSelectionUI == null)
            {
                CreateModelSelectionUI();
            }
            
            // 每次显示时重新扫描
            ScanAvailableNPCModels();
            
            // 清空旧列表
            foreach (Transform child in modelListPanel.transform)
            {
                Destroy(child.gameObject);
            }
            
            if (npcModelNames.Count == 0)
            {
                // 显示"没有可用模型"提示
                CreateNoModelsMessage();
                modelSelectionUI.SetActive(true);
                isUIVisible = true;
                return;
            }
            
            // 创建模型按钮
            for (int i = 0; i < npcModelNames.Count; i++)
            {
                string modelName = npcModelNames[i];
                CreateModelButton(modelName, () => {
                    SwapToNPCModel(modelName);
                    HideModelSelectionUI();
                }, false);
            }
            
            modelSelectionUI.SetActive(true);
            isUIVisible = true;
            
            // 显示鼠标光标
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            
            Debug.Log($"[NPC模型替换] UI已显示，共 {npcModelNames.Count} 个模型");
        }
        
        /// <summary>
        /// 隐藏模型选择UI
        /// </summary>
        private void HideModelSelectionUI()
        {
            if (modelSelectionUI != null)
            {
                modelSelectionUI.SetActive(false);
                isUIVisible = false;
                
                Debug.Log("[NPC模型替换] UI已隐藏");
            }
        }
        
        /// <summary>
        /// 创建模型按钮
        /// </summary>
        private void CreateModelButton(string modelName, UnityEngine.Events.UnityAction onClick, bool isSpecial)
        {
            GameObject button = new GameObject($"Button_{modelName}");
            button.transform.SetParent(modelListPanel.transform, false);
            
            RectTransform buttonRect = button.AddComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(0, 60);
            
            Image buttonImage = button.AddComponent<Image>();
            
            // 设置按钮背景颜色
            buttonImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            
            Button buttonComponent = button.AddComponent<Button>();
            
            // 设置按钮颜色状态 - 更显眼的高亮效果
            ColorBlock colors = buttonComponent.colors;
            colors.normalColor = new Color(0.2f, 0.2f, 0.2f, 1f);           // 深灰色
            colors.highlightedColor = new Color(0.4f, 0.7f, 1f, 1f);        // 明亮的蓝色
            colors.pressedColor = new Color(0.2f, 0.8f, 0.3f, 1f);          // 绿色
            colors.selectedColor = new Color(0.4f, 0.7f, 1f, 1f);           // 蓝色
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            colors.colorMultiplier = 1.5f;  // 增加颜色强度
            colors.fadeDuration = 0.1f;     // 快速过渡
            buttonComponent.colors = colors;
            
            buttonComponent.onClick.AddListener(onClick);
            
            // 添加文本
            GameObject text = new GameObject("Text");
            text.transform.SetParent(button.transform, false);
            
            RectTransform textRect = text.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);
            
            TextMeshProUGUI textComponent = text.AddComponent<TextMeshProUGUI>();
            textComponent.text = modelName;
            textComponent.fontSize = 24;
            textComponent.alignment = TextAlignmentOptions.Left;
            textComponent.color = Color.white;
            textComponent.enableWordWrapping = false;
            textComponent.overflowMode = TextOverflowModes.Ellipsis;
        }
        
        /// <summary>
        /// 创建分隔线
        /// </summary>
        private void CreateSeparator()
        {
            GameObject separator = new GameObject("Separator");
            separator.transform.SetParent(modelListPanel.transform, false);
            
            RectTransform sepRect = separator.AddComponent<RectTransform>();
            sepRect.sizeDelta = new Vector2(0, 2);
            
            Image sepImage = separator.AddComponent<Image>();
            sepImage.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        }
        
        /// <summary>
        /// 创建"没有模型"提示
        /// </summary>
        private void CreateNoModelsMessage()
        {
            GameObject message = new GameObject("NoModelsMessage");
            message.transform.SetParent(modelListPanel.transform, false);
            
            RectTransform msgRect = message.AddComponent<RectTransform>();
            msgRect.sizeDelta = new Vector2(0, 100);
            
            TextMeshProUGUI msgText = message.AddComponent<TextMeshProUGUI>();
            msgText.text = "当前场景没有可用的NPC模型\n\n请进入战斗关卡后再试";
            msgText.fontSize = 24;
            msgText.alignment = TextAlignmentOptions.Center;
            msgText.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        }
        
        /// <summary>
        /// 将玩家模型替换为指定的NPC模型
        /// </summary>
        private void SwapToNPCModel(string modelName)
        {
            if (playerCharacter == null)
            {
                Debug.LogWarning("[NPC模型替换] 玩家角色不存在");
                return;
            }
            
            if (!availableNPCModels.ContainsKey(modelName))
            {
                Debug.LogWarning($"[NPC模型替换] 模型不存在: {modelName}");
                return;
            }
            
            CharacterModel targetModel = availableNPCModels[modelName];
            
            if (targetModel == null)
            {
                Debug.LogError("[NPC模型替换] 目标模型为空");
                return;
            }
            
            // 实例化NPC模型
            GameObject modelInstance = Instantiate(targetModel.gameObject);
            CharacterModel newModel = modelInstance.GetComponent<CharacterModel>();
            
            if (newModel == null)
            {
                Debug.LogError("[NPC模型替换] 无法获取模型组件");
                Destroy(modelInstance);
                return;
            }
            
            // 设置新模型的父对象和位置
            newModel.transform.SetParent(playerCharacter.transform);
            newModel.transform.localPosition = Vector3.zero;
            newModel.transform.localRotation = Quaternion.identity;
            newModel.transform.localScale = Vector3.one;
            
            // 销毁之前的NPC模型（如果有）
            if (currentNPCModel != null)
            {
                Destroy(currentNPCModel.gameObject);
                Debug.Log("[NPC模型替换] 已销毁之前的NPC模型");
            }
            
            // 使用CharacterMainControl的SetCharacterModel方法
            playerCharacter.SetCharacterModel(newModel);
            
            currentNPCModel = newModel;
            
            // 如果NPC模型有Team信息，将玩家队伍改为NPC的队伍（让敌人攻击你）
            if (targetModel.characterMainControl != null)
            {
                Teams npcTeam = targetModel.characterMainControl.Team;
                Debug.Log($"[NPC模型替换] NPC原始队伍: {npcTeam}");
                
                // 如果NPC是敌对队伍（scav/pmc/boss等），将玩家也改为该队伍
                if (npcTeam != Teams.player)
                {
                    playerCharacter.SetTeam(npcTeam);
                    Debug.Log($"[NPC模型替换] 已将玩家队伍改为: {npcTeam} (敌人会攻击你)");
                    playerCharacter.PopText($"已切换模型和队伍: {npcTeam}");
                }
                else
                {
                    playerCharacter.PopText($"已切换模型: {modelName}");
                }
            }
            else
            {
                playerCharacter.PopText($"已切换模型: {modelName}");
            }
            
            Debug.Log($"[NPC模型替换] 成功替换为模型: {modelName}");
        }
        
        private void Update()
        {
            // ========== 保持鼠标显示（当UI可见时） ==========
            if (isUIVisible)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            
            // ========== NPC模型替换快捷键 ==========
            // * (星号键) - 显示/隐藏模型选择UI
            // 支持小键盘*或Shift+8（主键盘*）
            bool openKeyPressed = Input.GetKeyDown(KeyCode.KeypadMultiply) || 
                                  (Input.GetKeyDown(KeyCode.Alpha8) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)));
            
            if (openKeyPressed)
            {
                if (isUIVisible)
                {
                    HideModelSelectionUI();
                }
                else
                {
                    ShowModelSelectionUI();
                }
            }
            
            // ESC - 关闭UI
            if (Input.GetKeyDown(KeyCode.Escape) && isUIVisible)
            {
                HideModelSelectionUI();
            }
        }
        
        private void OnDestroy()
        {
            // 取消监听场景加载事件
            SceneManager.sceneLoaded -= OnSceneLoaded;
            
            // 清理UI资源
            if (modelSelectionUI != null)
            {
                GameObject.Destroy(modelSelectionUI);
            }
            
            // 清理字典
            availableNPCModels.Clear();
            npcModelNames.Clear();
            
            Debug.Log("[NPC模型替换] Mod已卸载");
        }
    }
}
