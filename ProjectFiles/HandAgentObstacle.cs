using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HandAgentObstacle : MonoBehaviour
{
    public GameObject agent;
    void OnTriggerEnter(Collider other)
    {
        {
            agent.GetComponent<HandAgent>().setBadTargetHit(true);
        }
    }
}
