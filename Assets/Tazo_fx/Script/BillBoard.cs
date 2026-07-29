using UnityEngine;
using System.Collections;

namespace TazoScript
{
    public class BillBoard : MonoBehaviour
    {

        // Use this for initialization
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            this.transform.rotation = Camera.main.transform.rotation;
        }
    }
}

