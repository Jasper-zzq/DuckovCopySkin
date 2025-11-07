using UnityEngine;
using System.Reflection;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using System.Collections.Generic;
using Duckov.UI;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace DuckovCopySkin
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private GameObject customButton;
        private Button btn_Transmog;
        private Item lastDisplayedItem;
        
        // 幻化系统相关
        private Item transmogSourceItem;  // 正在进行幻化的源装备
        private bool isSelectingTransmogAppearance = false;  // 是否正在选择幻化外观
        
        // CustomData的Key
        private const string TRANSMOG_ITEM_NAME_KEY = "TransmogItemName";
        private const string TRANSMOG_ITEM_ID_KEY = "TransmogItemID";
        
        // 用于临时存储原始外观的字典
        private Dictionary<Item, Sprite> originalIcons = new Dictionary<Item, Sprite>();
        private Dictionary<Item, ItemAgent> originalEquipmentAgents = new Dictionary<Item, ItemAgent>();
        
        private void Awake()
        {
            Debug.Log("[幻化Mod] 已加载");
            
            // 监听场景加载事件
            SceneManager.sceneLoaded += OnSceneLoaded;
            
            // 延迟检查并恢复所有幻化
            restoreCoroutine = StartCoroutine(RestoreAllTransmogsOnLoad());
            
            // 监听装备槽变化，自动维护幻化
            monitorCoroutine = StartCoroutine(MonitorEquipmentSlots());
        }
        
        /// <summary>
        /// 场景加载时的回调
        /// </summary>
        private Coroutine restoreCoroutine = null;
        private Coroutine monitorCoroutine = null;
        
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"[幻化Mod] ===== 场景已加载: {scene.name}, 模式: {mode} =====");
            
            // 停止之前的协程
            if (restoreCoroutine != null)
            {
                StopCoroutine(restoreCoroutine);
                restoreCoroutine = null;
            }
            if (monitorCoroutine != null)
            {
                StopCoroutine(monitorCoroutine);
                monitorCoroutine = null;
            }
            
            // 清理之前的监听（使用-=操作符是安全的，即使没有注册也不会报错）
            CleanupEventListeners();
            
            // 清理字典（新场景需要重新保存原始外观）
            originalIcons.Clear();
            originalEquipmentAgents.Clear();
            
            // 重新开始恢复流程
            restoreCoroutine = StartCoroutine(RestoreAllTransmogsOnLoad());
            monitorCoroutine = StartCoroutine(MonitorEquipmentSlots());
        }
        
        /// <summary>
        /// 清理所有事件监听器
        /// </summary>
        private void CleanupEventListeners()
        {
            var character = CharacterMainControl.Main;
            if (character != null && character.CharacterItem != null && character.CharacterItem.Slots != null)
            {
                foreach (var slot in character.CharacterItem.Slots)
                {
                    if (slot != null)
                    {
                        slot.onSlotContentChanged -= OnEquipmentSlotChanged;
                    }
                }
            }
        }
        
        /// <summary>
        /// 监听装备槽变化，自动维护幻化外观（直接注册）
        /// </summary>
        private IEnumerator MonitorEquipmentSlots()
        {
            var character = CharacterMainControl.Main;
            if (character == null || character.CharacterItem == null)
            {
                Debug.LogWarning("[幻化Mod] 无法监听装备槽");
                yield break;
            }
            
            // 注册所有装备槽的事件
            if (character.CharacterItem.Slots != null)
            {
                foreach (var slot in character.CharacterItem.Slots)
                {
                    if (slot != null)
                    {
                        slot.onSlotContentChanged += OnEquipmentSlotChanged;
                        Debug.Log($"[幻化Mod] 已监听插槽: {slot.Key}");
                    }
                }
            }
        }
        
        /// <summary>
        /// 装备槽内容变化时的回调
        /// </summary>
        private bool isRefreshing = false;  // 防止递归标志
        private void OnEquipmentSlotChanged(Slot slot)
        {
            // 防止递归调用
            if (isRefreshing)
                return;
            
            if (slot == null || slot.Content == null)
                return;
            
            var item = slot.Content;
            
            // 检查是否有幻化标记
            if (HasTransmog(item))
            {
                Debug.Log($"[幻化Mod] 装备槽变化，检测到幻化装备: {item.DisplayName}");
                
                string transmogIdStr = item.Variables.GetString(TRANSMOG_ITEM_ID_KEY, "");
                string transmogName = item.Variables.GetString(TRANSMOG_ITEM_NAME_KEY, "");
                
                if (!string.IsNullOrEmpty(transmogIdStr) && int.TryParse(transmogIdStr, out int transmogId))
                {
                    // 重新应用幻化
                    StartCoroutine(RestoreTransmogAppearance(item, transmogId, transmogName));
                }
            }
        }
        
        /// <summary>
        /// 游戏加载时恢复所有幻化外观（直接加载，无延迟）
        /// </summary>
        private IEnumerator RestoreAllTransmogsOnLoad()
        {
            Debug.Log("[幻化Mod] 开始立即恢复幻化...");
            
            // 直接获取角色，不等待
            var character = CharacterMainControl.Main;
            
            if (character == null)
            {
                Debug.LogWarning("[幻化Mod] 未找到角色，无法恢复幻化");
                yield break;
            }
            
            if (character.CharacterItem == null || character.CharacterItem.Slots == null)
            {
                Debug.LogWarning("[幻化Mod] 角色插槽未找到");
                yield break;
            }
            
            int restoredCount = 0;
            
            // 检查每个插槽中的装备
            foreach (var slot in character.CharacterItem.Slots)
            {
                if (slot == null || slot.Content == null)
                    continue;
                
                var item = slot.Content;
                
                Debug.Log($"[幻化Mod] 检查装备: {item.DisplayName} (插槽: {slot.Key})");
                
                // 检查是否有幻化标记
                if (HasTransmog(item))
                {
                    string transmogName = item.Variables.GetString(TRANSMOG_ITEM_NAME_KEY, "");
                    string transmogIdStr = item.Variables.GetString(TRANSMOG_ITEM_ID_KEY, "");
                    
                    Debug.Log($"[幻化Mod] 发现幻化标记: {transmogName} (ID: {transmogIdStr})");
                    
                    if (!string.IsNullOrEmpty(transmogIdStr) && int.TryParse(transmogIdStr, out int transmogId))
                    {
                        Debug.Log($"[幻化Mod] 准备恢复幻化: {item.DisplayName} -> {transmogName} (ID:{transmogId})");
                        
                        // 根据ID创建幻化外观物品（保留异步加载等待，这是必须的）
                        yield return RestoreTransmogAppearance(item, transmogId, transmogName);
                        restoredCount++;
                    }
                }
            }
            
            if (restoredCount > 0)
            {
                Debug.Log($"[幻化Mod] ===== 已成功恢复 {restoredCount} 个幻化装备 =====");
            }
            else
            {
                Debug.Log("[幻化Mod] 没有发现需要恢复的幻化装备");
            }
        }
        
        /// <summary>
        /// 恢复单个物品的幻化外观（直接加载）
        /// </summary>
        private IEnumerator RestoreTransmogAppearance(Item sourceItem, int transmogTypeId, string transmogName)
        {
            Item appearanceItem = null;
            
            // 使用协程等待异步加载完成（这个必须保留，Unity API要求）
            var loadTask = ItemStatsSystem.ItemAssetsCollection.InstantiateAsync(transmogTypeId);
            
            // 等待加载完成
            while (!loadTask.Status.IsCompleted())
            {
                yield return null;
            }
            
            // 获取结果
            appearanceItem = loadTask.GetAwaiter().GetResult();
            
            if (appearanceItem == null)
            {
                Debug.LogError($"[幻化Mod] 无法创建幻化外观物品: ID={transmogTypeId}");
                yield break;
            }
            
            Debug.Log($"[幻化Mod] 创建临时幻化物品: {appearanceItem.DisplayName}");
            
            // 保存原始外观（如果还没保存的话）
            if (!originalIcons.ContainsKey(sourceItem) && sourceItem.Icon != null)
            {
                originalIcons[sourceItem] = sourceItem.Icon;
                Debug.Log($"[幻化Mod] 保存原始图标（恢复时）");
            }
            
            if (!originalEquipmentAgents.ContainsKey(sourceItem))
            {
                int equipmentModelHash = "EquipmentModel".GetHashCode();
                var originalAgent = sourceItem.AgentUtilities.GetPrefab(equipmentModelHash);
                if (originalAgent != null)
                {
                    originalEquipmentAgents[sourceItem] = originalAgent;
                    Debug.Log($"[幻化Mod] 保存原始装备Agent（恢复时）");
                }
            }
            
            Debug.Log($"[幻化Mod] 开始应用外观，源装备Icon: {sourceItem.Icon?.name}");
            
            // 复制外观
            if (appearanceItem.Icon != null)
            {
                sourceItem.Icon = appearanceItem.Icon;
                Debug.Log($"[幻化Mod] 已设置图标: {appearanceItem.Icon.name}");
            }
            
            // 复制装备Agent
            CopyEquipmentAgent(sourceItem, appearanceItem);
            
            // 销毁临时物品
            UnityEngine.Object.Destroy(appearanceItem.gameObject);
            
            // 刷新显示
            RefreshItemDisplay(sourceItem);
            
            Debug.Log($"[幻化Mod] ===== 已恢复幻化: {sourceItem.DisplayName} -> {transmogName} =====");
        }
        
        private void Update()
        {
            // 检查 ItemOperationMenu 是否存在并且打开
            if (ItemOperationMenu.Instance == null)
                return;
            
            // 使用反射获取私有字段
            var fadeGroupField = typeof(ItemOperationMenu).GetField("fadeGroup", BindingFlags.NonPublic | BindingFlags.Instance);
            var displayingItemField = typeof(ItemOperationMenu).GetField("displayingItem", BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (fadeGroupField == null || displayingItemField == null)
                return;
            
            var fadeGroup = fadeGroupField.GetValue(ItemOperationMenu.Instance) as Duckov.UI.Animations.FadeGroup;
            var displayingItem = displayingItemField.GetValue(ItemOperationMenu.Instance) as Item;
            
            // 【优先处理】检查是否处于选择幻化外观模式
            // 在菜单完全显示之前就拦截，实现"点击即幻化"的效果
            if (isSelectingTransmogAppearance && transmogSourceItem != null && displayingItem != null)
            {
                Debug.Log($"[幻化Mod] 处于选择模式，检测到物品: {displayingItem?.DisplayName}，立即拦截");
                
                // 立即关闭菜单，防止显示物品信息
                ItemOperationMenu.Instance.Close();
                
                // 不能选择源装备本身
                if (displayingItem == transmogSourceItem)
                {
                    var character = CharacterMainControl.Main;
                    if (character != null)
                    {
                        character.PopText("不能选择自己作为外观！");
                    }
                    
                    // 退出选择模式
                    isSelectingTransmogAppearance = false;
                    transmogSourceItem = null;
                    lastDisplayedItem = null;
                    return;
                }
                
                // 检查是否为同类别物品
                if (IsSameCategory(transmogSourceItem, displayingItem))
                {
                    Debug.Log($"[幻化Mod] 类别匹配，应用幻化: {transmogSourceItem.DisplayName} -> {displayingItem.DisplayName}");
                    
                    // 应用幻化
                    ApplyTransmog(transmogSourceItem, displayingItem);
                    
                    // 退出选择模式
                    isSelectingTransmogAppearance = false;
                    transmogSourceItem = null;
                    lastDisplayedItem = null;
                    return;
                }
                else
                {
                    // 类别不匹配，给出提示
                    var character = CharacterMainControl.Main;
                    if (character != null)
                    {
                        character.PopText("物品类别不匹配！");
                    }
                    Debug.Log($"[幻化Mod] 类别不匹配: {transmogSourceItem.PluggedIntoSlot?.Key} vs {displayingItem.PluggedIntoSlot?.Key}");
                    
                    // 退出选择模式
                    isSelectingTransmogAppearance = false;
                    transmogSourceItem = null;
                    lastDisplayedItem = null;
                    return;
                }
            }
            
            // 检查菜单是否显示（正常模式）
            if (fadeGroup == null || !fadeGroup.IsShown || displayingItem == null)
            {
                // 菜单未显示，清理按钮
                if (customButton != null)
                {
                    customButton.SetActive(false);
                }
                lastDisplayedItem = null;
                return;
            }
            
            // 如果是同一个物品，不需要重新设置
            if (displayingItem == lastDisplayedItem)
                return;
            
            lastDisplayedItem = displayingItem;
            
            // 检查物品是否属于指定类型
            if (ShouldShowTransmogButton(displayingItem))
            {
                // 显示幻化按钮
                EnsureCustomButtonExists();
                if (customButton != null)
                {
                    customButton.SetActive(true);
                    // 更新按钮文本
                    UpdateButtonText(displayingItem);
                    // 更新物品描述，显示幻化信息
                    UpdateItemDescription(displayingItem);
                }
            }
            else
            {
                // 隐藏幻化按钮
                if (customButton != null)
                {
                    customButton.SetActive(false);
                }
            }
        }
        
        /// <summary>
        /// 判断物品是否应该显示幻化按钮（仅装备，不包括武器）
        /// </summary>
        private bool ShouldShowTransmogButton(Item item)
        {
            if (item == null)
                return false;
            
            // 如果物品在插槽中，检查插槽类型
            if (item.PluggedIntoSlot != null)
            {
                string slotKey = item.PluggedIntoSlot.Key;
                
                // 装备类插槽（不包括武器）
                bool isEquipment = slotKey == "Helmat" || slotKey == "Armor" || 
                                   slotKey == "FaceMask" || slotKey == "Headset" || 
                                   slotKey == "Backpack";
                
                if (isEquipment)
                {
                    Debug.Log($"[幻化Mod] 装备检查: {item.DisplayName}, 插槽:{slotKey}, 显示按钮:true");
                }
                
                return isEquipment;
            }
            
            // 如果物品在背包中，通过标签判断（不包括武器）
            var tags = item.Tags;
            if (tags != null)
            {
                bool isEquipment = tags.Contains("Helmat") || tags.Contains("Helmet") ||
                                   tags.Contains("Armor") || tags.Contains("Body") ||
                                   tags.Contains("FaceMask") || tags.Contains("Mask") ||
                                   tags.Contains("Headset") || tags.Contains("Backpack") || tags.Contains("Bag");
                
                return isEquipment;
            }
            
            return false;
        }
        
        /// <summary>
        /// 确保自定义按钮存在
        /// </summary>
        private void EnsureCustomButtonExists()
        {
            if (customButton != null)
                return;
            
            try
            {
                // 获取原有按钮作为模板
                var btn_ModifyField = typeof(ItemOperationMenu).GetField("btn_Modify", BindingFlags.NonPublic | BindingFlags.Instance);
                if (btn_ModifyField == null)
                    return;
                
                var btn_Modify = btn_ModifyField.GetValue(ItemOperationMenu.Instance) as Button;
                if (btn_Modify == null)
                    return;
                
                // 复制按钮
                customButton = GameObject.Instantiate(btn_Modify.gameObject, btn_Modify.transform.parent);
                customButton.name = "btn_Transmog";
                
                // 获取按钮组件
                btn_Transmog = customButton.GetComponent<Button>();
                if (btn_Transmog == null)
                    return;
                
                // 清除原有的监听器
                btn_Transmog.onClick.RemoveAllListeners();
                
                // 添加新的点击事件
                btn_Transmog.onClick.AddListener(OnTransmogButtonClicked);
                
                // 调试：输出按钮上的所有组件
                Debug.Log("[幻化Mod] 按钮上的所有组件:");
                foreach (var component in customButton.GetComponents<Component>())
                {
                    Debug.Log($"  - {component.GetType().Name}");
                }
                
                // 查找并禁用可能影响文本的本地化组件
                var localizationComponents = customButton.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var comp in localizationComponents)
                {
                    var typeName = comp.GetType().Name;
                    // 禁用可能的本地化组件
                    if (typeName.Contains("Localiz") || typeName.Contains("Text"))
                    {
                        Debug.Log($"[幻化Mod] 找到组件: {typeName} 在 {comp.gameObject.name}");
                        // 尝试禁用本地化组件（如果存在）
                        if (typeName.Contains("Localiz"))
                        {
                            comp.enabled = false;
                            Debug.Log($"[幻化Mod] 已禁用本地化组件: {typeName}");
                        }
                    }
                }
                
                // 设置文本
                StartCoroutine(SetButtonTextDelayed());
                
                // 将按钮移动到列表底部
                customButton.transform.SetAsLastSibling();
                
                Debug.Log("[幻化Mod] 自定义按钮已创建");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[幻化Mod] 创建自定义按钮时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 延迟设置按钮文本（等待一帧后再设置，避免被其他逻辑覆盖）
        /// </summary>
        private IEnumerator SetButtonTextDelayed()
        {
            // 等待一帧
            yield return null;
            
            if (customButton != null)
            {
                // 获取所有TextMeshProUGUI组件
                var textComponents = customButton.GetComponentsInChildren<TextMeshProUGUI>(true);
                Debug.Log($"[幻化Mod] 找到 {textComponents.Length} 个TextMeshProUGUI组件");
                
                foreach (var textComp in textComponents)
                {
                    Debug.Log($"[幻化Mod] 设置文本前: GameObject={textComp.gameObject.name}, 当前文本='{textComp.text}'");
                    textComp.text = "幻化";
                    Debug.Log($"[幻化Mod] 设置文本后: 新文本='{textComp.text}'");
                }
                
                // 再等待一帧，检查是否被覆盖
                yield return null;
                
                foreach (var textComp in textComponents)
                {
                    Debug.Log($"[幻化Mod] 一帧后检查: GameObject={textComp.gameObject.name}, 文本='{textComp.text}'");
                    if (textComp.text != "幻化")
                    {
                        Debug.LogWarning($"[幻化Mod] 警告：文本被覆盖了！重新设置...");
                        textComp.text = "幻化";
                    }
                }
            }
        }
        
        /// <summary>
        /// 检查物品是否已经有幻化
        /// </summary>
        private bool HasTransmog(Item item)
        {
            if (item == null || item.Variables == null)
                return false;
            
            return item.Variables.GetEntry(TRANSMOG_ITEM_NAME_KEY) != null;
        }
        
        /// <summary>
        /// 判断两个物品是否为同一类别（仅装备）
        /// </summary>
        private bool IsSameCategory(Item item1, Item item2)
        {
            if (item1 == null || item2 == null)
                return false;
            
            // 获取物品的插槽类型（支持已装备和未装备的物品）
            string slot1 = GetItemSlotType(item1);
            string slot2 = GetItemSlotType(item2);
            
            if (string.IsNullOrEmpty(slot1) || string.IsNullOrEmpty(slot2))
            {
                Debug.LogWarning($"[幻化Mod] 无法确定物品类别: {item1.DisplayName} ({slot1}) vs {item2.DisplayName} ({slot2})");
                return false;
            }
            
            Debug.Log($"[幻化Mod] 类别比较: {item1.DisplayName} ({slot1}) vs {item2.DisplayName} ({slot2})");
            
            return slot1 == slot2;
        }
        
        /// <summary>
        /// 获取物品的插槽类型（支持已装备和未装备的物品，仅装备）
        /// </summary>
        private string GetItemSlotType(Item item)
        {
            if (item == null)
                return null;
            
            // 如果物品已经装备，直接返回插槽类型（排除武器）
            if (item.PluggedIntoSlot != null)
            {
                string slotKey = item.PluggedIntoSlot.Key;
                // 只返回装备类型，不返回武器
                if (slotKey != "Weapon")
                    return slotKey;
                return null;
            }
            
            // 如果物品在背包中，通过标签（Tags）判断类型
            var tags = item.Tags;
            if (tags != null)
            {
                // 检查常见的装备类型标签（不包括武器）
                if (tags.Contains("Helmat") || tags.Contains("Helmet"))
                    return "Helmat";  // 注意游戏中的拼写
                if (tags.Contains("Armor") || tags.Contains("Body"))
                    return "Armor";
                if (tags.Contains("FaceMask") || tags.Contains("Mask"))
                    return "FaceMask";
                if (tags.Contains("Headset"))
                    return "Headset";
                if (tags.Contains("Backpack") || tags.Contains("Bag"))
                    return "Backpack";
            }
            
            Debug.LogWarning($"[幻化Mod] 无法识别物品类型: {item.DisplayName}, Tags: {string.Join(", ", tags?.Select(t => t?.name) ?? new string[0])}");
            return null;
        }
        
        /// <summary>
        /// 更新按钮文本
        /// </summary>
        private void UpdateButtonText(Item item)
        {
            if (customButton == null)
                return;
            
            string buttonText = HasTransmog(item) ? "移除幻化" : "幻化";
            
            var textComponents = customButton.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var textComp in textComponents)
            {
                textComp.text = buttonText;
            }
        }
        
        /// <summary>
        /// 更新物品描述，显示幻化信息
        /// </summary>
        private void UpdateItemDescription(Item item)
        {
            if (item == null || ItemOperationMenu.Instance == null)
                return;
            
            try
            {
                // 使用反射获取描述文本组件
                var txt_DescriptionField = typeof(ItemOperationMenu).GetField("txt_Description", BindingFlags.NonPublic | BindingFlags.Instance);
                if (txt_DescriptionField == null)
                {
                    Debug.LogWarning("[幻化Mod] 未找到txt_Description字段");
                    return;
                }
                
                var txt_Description = txt_DescriptionField.GetValue(ItemOperationMenu.Instance) as TextMeshProUGUI;
                if (txt_Description == null)
                {
                    Debug.LogWarning("[幻化Mod] txt_Description为空");
                    return;
                }
                
                // 获取当前描述文本
                string originalDescription = txt_Description.text;
                
                // 移除之前添加的幻化信息（如果有）
                int transmogIndex = originalDescription.IndexOf("\n\n<color=#00FFFF>幻化时装:");
                if (transmogIndex >= 0)
                {
                    originalDescription = originalDescription.Substring(0, transmogIndex);
                }
                
                // 如果物品有幻化,添加幻化信息
                if (HasTransmog(item))
                {
                    string transmogName = item.Variables.GetString(TRANSMOG_ITEM_NAME_KEY, "未知");
                    originalDescription += $"\n\n<color=#00FFFF>幻化时装: {transmogName}</color>";
                }
                
                // 更新描述文本
                txt_Description.text = originalDescription;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[幻化Mod] 更新物品描述时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 应用幻化
        /// 重要：只复制外观，保留源装备的所有属性和功能
        /// </summary>
        private void ApplyTransmog(Item sourceItem, Item appearanceItem)
        {
            if (sourceItem == null || appearanceItem == null)
                return;
            
            try
            {
                // 保存幻化信息到源装备
                if (sourceItem.Variables == null)
                {
                    Debug.LogError("[幻化Mod] Variables为空");
                    return;
                }
                
                // 保存幻化记录（用于显示"幻化时装: XXX"）
                sourceItem.Variables.SetString(TRANSMOG_ITEM_NAME_KEY, appearanceItem.DisplayName, true);
                sourceItem.Variables.SetString(TRANSMOG_ITEM_ID_KEY, appearanceItem.TypeID.ToString(), true);
                
                // ============================================
                // 保存原始外观（用于移除幻化时恢复）
                // ============================================
                if (!originalIcons.ContainsKey(sourceItem))
                {
                    originalIcons[sourceItem] = sourceItem.Icon;
                }
                
                if (!originalEquipmentAgents.ContainsKey(sourceItem))
                {
                    // 保存原始的EquipmentModel Agent
                    int equipmentModelHash = "EquipmentModel".GetHashCode();
                    var originalAgent = sourceItem.AgentUtilities.GetPrefab(equipmentModelHash);
                    if (originalAgent != null)
                    {
                        originalEquipmentAgents[sourceItem] = originalAgent;
                        Debug.Log($"[幻化Mod] 保存原始装备Agent: {originalAgent.name}");
                    }
                }
                
                // ============================================
                // 【仅复制外观】不影响任何属性和功能
                // 保留：Stats（属性）、Modifiers（词条）、Effects（特效）、
                //      Durability（耐久）、DisplayName（名称）等所有功能性数据
                // ============================================
                
                // 复制图标（背包和界面显示）
                if (appearanceItem.Icon != null)
                {
                    sourceItem.Icon = appearanceItem.Icon;
                    Debug.Log($"[幻化Mod] 复制图标: {appearanceItem.Icon.name}");
                }
                
                // 复制3D模型预制体（角色身上的装备显示）
                // 装备的3D模型是通过 AgentUtilities 的 "EquipmentModel" Agent创建的
                CopyEquipmentAgent(sourceItem, appearanceItem);
                
                Debug.Log($"[幻化Mod] 幻化成功: {sourceItem.DisplayName} -> {appearanceItem.DisplayName}");
                
                // 提示玩家
                var character = CharacterMainControl.Main;
                if (character != null)
                {
                    character.PopText($"幻化成功: {appearanceItem.DisplayName}");
                }
                
                // 延迟刷新物品显示（确保3D模型更新）
                StartCoroutine(DelayedRefreshItem(sourceItem));
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[幻化Mod] 应用幻化时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 移除幻化
        /// 恢复装备的原始外观，属性和功能不受影响
        /// </summary>
        private void RemoveTransmog(Item item)
        {
            if (item == null || !HasTransmog(item))
                return;
            
            try
            {
                // 移除幻化数据
                var transmogNameEntry = item.Variables.GetEntry(TRANSMOG_ITEM_NAME_KEY);
                if (transmogNameEntry != null)
                {
                    item.Variables.Remove(transmogNameEntry);
                }
                
                var transmogIdEntry = item.Variables.GetEntry(TRANSMOG_ITEM_ID_KEY);
                if (transmogIdEntry != null)
                {
                    item.Variables.Remove(transmogIdEntry);
                }
                
                // 恢复原始外观
                if (originalIcons.ContainsKey(item))
                {
                    item.Icon = originalIcons[item];
                    originalIcons.Remove(item);
                    Debug.Log($"[幻化Mod] 恢复原始图标");
                }
                
                // 恢复原始3D模型Agent
                RestoreEquipmentAgent(item);
                
                Debug.Log($"[幻化Mod] 移除幻化成功: {item.DisplayName}");
                
                // 提示玩家
                var character = CharacterMainControl.Main;
                if (character != null)
                {
                    character.PopText("已移除幻化");
                }
                
                // 延迟刷新物品显示（确保3D模型更新）
                StartCoroutine(DelayedRefreshItem(item));
                
                // 更新按钮文本
                UpdateButtonText(item);
                
                // 更新物品描述
                UpdateItemDescription(item);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[幻化Mod] 移除幻化时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        
        /// <summary>
        /// 复制装备Agent（3D模型）
        /// </summary>
        private void CopyEquipmentAgent(Item sourceItem, Item appearanceItem)
        {
            try
            {
                // 获取AgentUtilities的agents字段
                var agentsField = typeof(ItemAgentUtilities).GetField("agents", BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null)
                {
                    Debug.LogWarning("[幻化Mod] 未找到agents字段");
                    return;
                }
                
                // 获取源物品和外观物品的agents列表
                var sourceAgents = agentsField.GetValue(sourceItem.AgentUtilities) as System.Collections.IList;
                var appearanceAgents = agentsField.GetValue(appearanceItem.AgentUtilities) as System.Collections.IList;
                
                if (sourceAgents == null || appearanceAgents == null)
                {
                    Debug.LogWarning("[幻化Mod] agents列表为空");
                    return;
                }
                
                // 查找"EquipmentModel" Agent
                int equipmentModelHash = "EquipmentModel".GetHashCode();
                
                // 在外观物品中查找EquipmentModel Agent
                ItemAgent appearanceEquipmentAgent = appearanceItem.AgentUtilities.GetPrefab(equipmentModelHash);
                
                if (appearanceEquipmentAgent == null)
                {
                    Debug.LogWarning($"[幻化Mod] 外观物品没有EquipmentModel Agent: {appearanceItem.DisplayName}");
                    return;
                }
                
                Debug.Log($"[幻化Mod] 找到外观装备Agent: {appearanceEquipmentAgent.name}");
                
                // 修改源物品的EquipmentModel Agent
                // 获取AgentKeyPair类型
                var agentKeyPairType = typeof(ItemAgentUtilities).GetNestedType("AgentKeyPair", BindingFlags.Public);
                if (agentKeyPairType == null)
                {
                    Debug.LogWarning("[幻化Mod] 未找到AgentKeyPair类型");
                    return;
                }
                
                var keyField = agentKeyPairType.GetField("key");
                var agentPrefabField = agentKeyPairType.GetField("agentPrefab");
                
                // 查找并替换源物品的EquipmentModel
                bool found = false;
                foreach (var agentPair in sourceAgents)
                {
                    string key = keyField.GetValue(agentPair) as string;
                    if (key == "EquipmentModel")
                    {
                        agentPrefabField.SetValue(agentPair, appearanceEquipmentAgent);
                        found = true;
                        Debug.Log($"[幻化Mod] 已替换EquipmentModel Agent: {appearanceEquipmentAgent.name}");
                        break;
                    }
                }
                
                if (!found)
                {
                    Debug.LogWarning($"[幻化Mod] 源物品没有EquipmentModel Agent: {sourceItem.DisplayName}");
                }
                
                // 清除缓存，强制重建
                var hashedAgentsCacheField = typeof(ItemAgentUtilities).GetField("hashedAgentsCache", BindingFlags.NonPublic | BindingFlags.Instance);
                if (hashedAgentsCacheField != null)
                {
                    hashedAgentsCacheField.SetValue(sourceItem.AgentUtilities, null);
                    Debug.Log("[幻化Mod] 已清除AgentUtilities缓存");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[幻化Mod] 复制装备Agent时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 恢复原始装备Agent
        /// </summary>
        private void RestoreEquipmentAgent(Item item)
        {
            if (!originalEquipmentAgents.ContainsKey(item))
                return;
            
            try
            {
                var originalAgent = originalEquipmentAgents[item];
                
                // 获取AgentUtilities的agents字段
                var agentsField = typeof(ItemAgentUtilities).GetField("agents", BindingFlags.NonPublic | BindingFlags.Instance);
                if (agentsField == null)
                {
                    Debug.LogWarning("[幻化Mod] 未找到agents字段");
                    return;
                }
                
                var sourceAgents = agentsField.GetValue(item.AgentUtilities) as System.Collections.IList;
                if (sourceAgents == null)
                {
                    Debug.LogWarning("[幻化Mod] agents列表为空");
                    return;
                }
                
                // 获取AgentKeyPair类型
                var agentKeyPairType = typeof(ItemAgentUtilities).GetNestedType("AgentKeyPair", BindingFlags.Public);
                if (agentKeyPairType == null)
                {
                    Debug.LogWarning("[幻化Mod] 未找到AgentKeyPair类型");
                    return;
                }
                
                var keyField = agentKeyPairType.GetField("key");
                var agentPrefabField = agentKeyPairType.GetField("agentPrefab");
                
                // 查找并恢复源物品的EquipmentModel
                foreach (var agentPair in sourceAgents)
                {
                    string key = keyField.GetValue(agentPair) as string;
                    if (key == "EquipmentModel")
                    {
                        agentPrefabField.SetValue(agentPair, originalAgent);
                        Debug.Log($"[幻化Mod] 已恢复原始装备Agent: {originalAgent.name}");
                        break;
                    }
                }
                
                // 清除缓存
                var hashedAgentsCacheField = typeof(ItemAgentUtilities).GetField("hashedAgentsCache", BindingFlags.NonPublic | BindingFlags.Instance);
                if (hashedAgentsCacheField != null)
                {
                    hashedAgentsCacheField.SetValue(item.AgentUtilities, null);
                }
                
                originalEquipmentAgents.Remove(item);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[幻化Mod] 恢复装备Agent时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// 立即刷新物品显示
        /// </summary>
        private IEnumerator DelayedRefreshItem(Item item)
        {
            if (item != null && item.PluggedIntoSlot != null)
            {
                RefreshItemDisplay(item);
            }
            yield break;
        }
        
        /// <summary>
        /// 刷新物品显示（包括3D模型）
        /// 通过重新装备物品来强制刷新角色身上的装备模型
        /// </summary>
        private void RefreshItemDisplay(Item item)
        {
            if (item == null)
                return;
            
            // 如果物品在插槽中，通过拔出再插入来强制刷新3D模型
            if (item.PluggedIntoSlot != null)
            {
                var slot = item.PluggedIntoSlot;
                
                Debug.Log($"[幻化Mod] 开始刷新装备，插槽: {slot.Key}, 物品: {item.DisplayName}");
                Debug.Log($"[幻化Mod] 当前ItemGraphic: {item.ItemGraphic?.name}");
                
                // 设置标志，防止递归
                isRefreshing = true;
                
                try
                {
                    // 先拔出物品
                    var unpluggedItem = slot.Unplug();
                    
                    // 立即重新插入（这会触发ChangeXXXModel方法重新创建3D模型）
                    if (unpluggedItem != null)
                    {
                        Item discardedItem;
                        bool plugSuccess = slot.Plug(unpluggedItem, out discardedItem);
                        
                        Debug.Log($"[幻化Mod] 重新装备{(plugSuccess ? "成功" : "失败")}: {slot.Key}");
                        Debug.Log($"[幻化Mod] 装备后ItemGraphic: {item.ItemGraphic?.name}");
                    }
                }
                finally
                {
                    // 清除标志
                    isRefreshing = false;
                }
            }
            else
            {
                Debug.LogWarning($"[幻化Mod] 物品未装备，无法刷新3D模型: {item.DisplayName}");
            }
        }
        
        /// <summary>
        /// 幻化按钮点击事件
        /// </summary>
        private void OnTransmogButtonClicked()
        {
            Debug.Log("[幻化Mod] 幻化按钮被点击");
            
            if (lastDisplayedItem == null)
                return;
            
            Debug.Log($"[幻化Mod] 当前物品: {lastDisplayedItem.DisplayName}");
            
            // 检查是否已经有幻化
            if (HasTransmog(lastDisplayedItem))
            {
                // 移除幻化
                RemoveTransmog(lastDisplayedItem);
            }
            else
            {
                // 开始幻化流程
                transmogSourceItem = lastDisplayedItem;
                isSelectingTransmogAppearance = true;
                
                Debug.Log($"[幻化Mod] 进入选择外观模式，源装备: {transmogSourceItem?.DisplayName}, 状态: {isSelectingTransmogAppearance}");
                
                var character = CharacterMainControl.Main;
                if (character != null)
                {
                    character.PopText("请选择要幻化的外观");
                }
            }
            
            // 关闭菜单
            ItemOperationMenu.Instance.Close();
        }
        
        private void OnDestroy()
        {
            // 取消监听场景加载事件
            SceneManager.sceneLoaded -= OnSceneLoaded;
            
            // 停止所有协程
            if (restoreCoroutine != null)
            {
                StopCoroutine(restoreCoroutine);
            }
            if (monitorCoroutine != null)
            {
                StopCoroutine(monitorCoroutine);
            }
            
            // 取消监听装备槽事件
            CleanupEventListeners();
            Debug.Log("[幻化Mod] 已取消监听装备槽");
            
            // 清理资源
            if (customButton != null)
            {
                GameObject.Destroy(customButton);
            }
            
            // 清理字典
            originalIcons.Clear();
            originalEquipmentAgents.Clear();
            
            Debug.Log("[幻化Mod] 已卸载");
        }
    }
}