using StellarNet.Lite.Shared.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Panel_SetRoomConfig_RoomComItem : MonoBehaviour
{
    [SerializeField] private Toggle chooseTog;
    [SerializeField] private TMP_Text comNameText;

    public int ComId { get; private set; }

    public void Init(string comName, int comId)
    {
        chooseTog.isOn = false;
        comNameText.text = comName;
        ComId = comId;

        if (comId == ComponentIdConst.RoomSettings)
        {
            chooseTog.isOn = true;
            chooseTog.interactable = false;
        }
    }

    public bool IsChoose()
    {
        bool returnValue = chooseTog.isOn;


        return returnValue;
    }
}