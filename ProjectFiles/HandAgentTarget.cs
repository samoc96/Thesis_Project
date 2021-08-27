using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandAgentTarget : MonoBehaviour
{

    public GameObject agent;

    void OnTriggerEnter(Collider other)
    {
        agent.GetComponent<HandAgent>().setTargetHit(true);
        
    }
}
