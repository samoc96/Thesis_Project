using UnityEngine;
using MLAgents;
using Libdexmo.Unity.HandController;
using System;

public class HandAgent : Agent
{
    public GameObject hand;
    public Transform thumb;
    public Transform index;
    public Transform middle;
    public Transform ring;
    public Transform pinky;
    public GameObject target;
    public GameObject badTarget;
    public GameObject agent;
    public GameObject dextaController;
    public TMPro.TextMeshPro CumulativeRewardText;
    DexmoController dexmoController = new DexmoController();

    public bool targetHit = false;
    public bool badTargetHit = false;
    public int targetHitCount = 0;
    public int badTargetHitCount = 0;
    public bool begin = true;
    public bool targetReset = false;
    public bool targetResetReset = false;
    private float[] _keyboardControlledFingerBend = { 0, 0, 0, 0, 0 };
    private float[] _keyboardControlledThumb_Rotate = { 0};
    private float[] angleValues = { 0, 0, 0, 0, 0, 0 };
    private float[] zeroAngles = { 0, 0, 0, 0, 0, 0};
    private int episodeCount = 0;

    public float RandomDoubleBetween(float min, float max)
    {
        System.Random r = new System.Random();
        float number;
        number = Convert.ToSingle(r.NextDouble() * (max - min) + min);
        return number;
    }
    public void resetHand()
    {
        setAngleValue(zeroAngles);
    }
    public bool getBeginEpisode()
    {
        return begin;
    }
    public void setTargetHit(bool b)
    {
        targetHit = b;
    }
    public void resetTargetHit()
    {
        targetHit = false;
    }
    public void setBadTargetHit(bool b)
    {
        badTargetHit = b;
    }
    public void resetBadTargetHit()
    {
        badTargetHit = false;
    }
    public float[] getAngleValue()
    {
        return angleValues;
    }
    public void setAngleValue(float[] x)
    {
        angleValues = x;
    }
    public override void InitializeAgent()
    {
        base.InitializeAgent();
        SetResetParameters();
    }
    public override void CollectObservations()
    {
        AddVectorObs(thumb.transform.rotation.x);
        AddVectorObs(thumb.transform.rotation.y);
        AddVectorObs(thumb.transform.rotation.z);
        AddVectorObs(index.transform.rotation.z);
        AddVectorObs(middle.transform.rotation.z);
        AddVectorObs(ring.transform.rotation.z);
        AddVectorObs(pinky.transform.rotation.z);

        AddVectorObs(target.transform.localPosition.y);
        AddVectorObs(badTarget.transform.localPosition.y);
    }
    public override void AgentAction(float[] vectorAction)
    {
        if (begin)
        {
            resetHand();
            dextaController.GetComponent<DexmoController>().testFunction();
            begin = false;
        }
        else
        {
            vectorAction[0] = Mathf.Clamp01(vectorAction[0]);
            vectorAction[1] = Mathf.Clamp01(vectorAction[1]);
            vectorAction[2] = Mathf.Clamp01(vectorAction[2]);
            vectorAction[3] = Mathf.Clamp01(vectorAction[3]);
            vectorAction[4] = Mathf.Clamp01(vectorAction[4]);
            vectorAction[5] = Mathf.Clamp01(vectorAction[5]);
            setAngleValue(vectorAction);
            bool isTouched = dextaController.GetComponent<DexmoController>().getObjectTouched();

            if (targetHit)
            {
                resetHand();
                dexmoController.testFunction();
                if (targetReset)
                {
                    if (targetResetReset)
                    {
                        int k = 10000000;
                        while (k > 0)
                        {
                            k = k - 1;
                        }
                        begin = false;
                        targetReset = false;
                        targetResetReset = false;
                        if (badTargetHit)
                        {
                            targetHit = false;
                            badTargetHit = false;
                            dextaController.GetComponent<DexmoController>().setObjectTouched(false);
                            Done();
                        }
                        else
                        {
                            AddReward(1f);
                            targetHit = false;
                            dextaController.GetComponent<DexmoController>().setObjectTouched(false);
                            targetHitCount += 1;
                            Debug.Log("GOOD");
                            Done();                          
                        }                        
                    }
                    else
                    {
                        targetResetReset = true;
                    }
                }
                else
                {
                    targetReset = true;
                }
            }
            if (badTargetHit)
            {
                resetHand();
                dexmoController.testFunction();
                if (targetReset)
                {
                    if (targetResetReset)
                    {
                        int k = 10000000;
                        while (k > 0)
                        {
                            k = k - 1;
                        }
                        begin = false;
                        targetReset = false;
                        targetResetReset = false;
                        if (targetHit)
                        {
                            targetHit = false;
                            badTargetHit = false;
                            dextaController.GetComponent<DexmoController>().setObjectTouched(false);
                            Done();
                        }
                        else
                        {
                            AddReward(-1f);
                            badTargetHit = false;
                            dextaController.GetComponent<DexmoController>().setObjectTouched(false);
                            badTargetHitCount += 1;
                            Debug.Log("BAD");
                            Done();
                        }
                    }
                    else
                    {
                        targetResetReset = true;
                    }
                }
                else
                {
                    targetReset = true;
                }
            }
            CumulativeRewardText.SetText(GetCumulativeReward().ToString());
        }
    }
    public override float[] Heuristic()
    {
        float speed = 0.005f;
        if (begin)
        {
            for (int i = 0; i < 5; i++)
            {
                _keyboardControlledFingerBend[i] = 0;

            }
            _keyboardControlledThumb_Rotate[0] = 0;
        }
        else
        {
            //Bend Angles
            if (Input.GetKey(KeyCode.Alpha1))
            {
                _keyboardControlledFingerBend[0] += speed;
                _keyboardControlledFingerBend[0] = Mathf.Clamp01(_keyboardControlledFingerBend[0]);

            }
            else if (Input.GetKey(KeyCode.Alpha2))
            {
                _keyboardControlledFingerBend[0] -= speed;
                _keyboardControlledFingerBend[0] = Mathf.Clamp01(_keyboardControlledFingerBend[0]);

            }

            if (Input.GetKey(KeyCode.Alpha3))
            {
                _keyboardControlledFingerBend[1] += speed;
                _keyboardControlledFingerBend[1] = Mathf.Clamp01(_keyboardControlledFingerBend[1]);

            }
            else if (Input.GetKey(KeyCode.Alpha4))
            {
                _keyboardControlledFingerBend[1] -= speed;
                _keyboardControlledFingerBend[1] = Mathf.Clamp01(_keyboardControlledFingerBend[1]);

            }

            if (Input.GetKey(KeyCode.Alpha5))
            {
                _keyboardControlledFingerBend[2] += speed;
                _keyboardControlledFingerBend[2] = Mathf.Clamp01(_keyboardControlledFingerBend[2]);

            }
            else if (Input.GetKey(KeyCode.Alpha6))
            {
                _keyboardControlledFingerBend[2] -= speed;
                _keyboardControlledFingerBend[2] = Mathf.Clamp01(_keyboardControlledFingerBend[2]);

            }

            if (Input.GetKey(KeyCode.Alpha7))
            {
                _keyboardControlledFingerBend[3] += speed;
                _keyboardControlledFingerBend[3] = Mathf.Clamp01(_keyboardControlledFingerBend[3]);

            }
            else if (Input.GetKey(KeyCode.Alpha8))
            {
                _keyboardControlledFingerBend[3] -= speed;
                _keyboardControlledFingerBend[3] = Mathf.Clamp01(_keyboardControlledFingerBend[3]);
            }

            if (Input.GetKey(KeyCode.Alpha9))
            {
                _keyboardControlledFingerBend[4] += speed;
                _keyboardControlledFingerBend[4] = Mathf.Clamp01(_keyboardControlledFingerBend[4]);
            }
            else if (Input.GetKey(KeyCode.Alpha0))
            {
                _keyboardControlledFingerBend[4] -= speed;
                _keyboardControlledFingerBend[4] = Mathf.Clamp01(_keyboardControlledFingerBend[4]);
            }

            //Split Angles and Rotate Angle
            if (Input.GetKey(KeyCode.Q))
            {
                _keyboardControlledThumb_Rotate[0] += speed;
                _keyboardControlledThumb_Rotate[0] = Mathf.Clamp01(_keyboardControlledThumb_Rotate[0]);

            }
            else if (Input.GetKey(KeyCode.W))
            {
                _keyboardControlledThumb_Rotate[0] -= speed;
                _keyboardControlledThumb_Rotate[0] = Mathf.Clamp01(_keyboardControlledThumb_Rotate[0]);

            }         
        }

        float[] z = new float[_keyboardControlledFingerBend.Length + _keyboardControlledThumb_Rotate.Length];
        _keyboardControlledFingerBend.CopyTo(z, 0);
        _keyboardControlledThumb_Rotate.CopyTo(z, _keyboardControlledFingerBend.Length);

        return z;
    }
    public override void AgentReset()
    {
        begin = true;
        resetHand();
        if (begin)
        {
            dexmoController.testFunction();
            begin = false;
        }

        resetTargetHit();
        resetBadTargetHit();
        if (begin == false)
        {
            float target_y_position = RandomDoubleBetween(0.32f, 0.425f);
            float badTarget_y_position;
            if (target_y_position > 0.375f)
            {
                badTarget_y_position = target_y_position - 0.05f;
            }
            else
            {
                badTarget_y_position = target_y_position + 0.05f;

            }
            target.transform.localPosition = new Vector3(-0.362f, target_y_position, 0.4051429f);
            badTarget.transform.localPosition = new Vector3(-0.362f, badTarget_y_position, 0.4051429f);
            begin = true;
        }
        SetResetParameters();
    }

    public void SetResetParameters()
    {
        var fp = Academy.Instance.FloatProperties;
    }
}