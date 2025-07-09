using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace pff.Homestead
{
    public class ObjectFalling : MonoBehaviour
    {
        private Vector3 _startPos;
        private Vector3 _targetPos;
        private float _volocityX = 0;
        private float _volocityY = 0;
        private float _accumulateTime = 0;
        private float _totalTime = 0;
        private float _yAccumulate = 0;
        private bool _canMove = false;

        private float _topDelta = 5;
        private Vector3 _dir;

        private Action OnMoveCompleted;

        private LineRenderer _testRender;

        public void Init(Vector3 startPos, Vector3 targetPos, Action onMoveCompleted = null, float totalTime = 2f,
            float initialSpeedY = 0)
        {
            _testRender = GetComponent<LineRenderer>();

            _accumulateTime = 0;
            _targetPos = targetPos;
            //GameObject.CreatePrimitive(PrimitiveType.Cube).transform.position = targetPos;
            _startPos = startPos;
            OnMoveCompleted = onMoveCompleted;
            _totalTime = totalTime;
            //_yAccumulate = yAccumulate;
            //_totalTime = totalTime;

            targetPos.y = 0;
            startPos.y = 0;
            _dir = (targetPos - startPos).normalized;
            //dir = (targetPos - transform.position).normalized;
            //transform.LookAt(targetPos);
            float dis = Vector3.Distance(targetPos, startPos);
            //_totalTime = Mathf.Pow(Mathf.Abs((_targetPos.y - _startPos.y)) * 2 / _yAccumulate, 1.0f / 2);
            _yAccumulate = ((Mathf.Abs(_targetPos.y - _startPos.y) + _topDelta) - initialSpeedY * _totalTime) * 2 /
                           (_totalTime * _totalTime);
            _volocityX = dis / _totalTime;
            //向下自由落体 垂直方向初速度为0
            _volocityY = initialSpeedY;
            _canMove = true;

        }

        // Update is called once per frame
        void Update()
        {
            if (_canMove)
            {
                if (_accumulateTime < _totalTime)
                {
                    _accumulateTime += Time.deltaTime;
                    Vector3 moveX = _volocityX * _accumulateTime * _dir;
                    Vector3 move = _startPos + moveX;
                    float y = _volocityY * _accumulateTime - _yAccumulate * 0.5f * Mathf.Pow(_accumulateTime, 2);
                    move = move + Vector3.up * y;
                    transform.position = move;
                }
                else
                {
                    transform.position = _targetPos;
                    OnMoveCompleted?.Invoke();
                    _canMove = false;
                }

                AddPoint();
            }
        }

        private void AddPoint()
        {
            _testRender.positionCount += 1;
            _testRender.SetPosition(_testRender.positionCount - 1, transform.position);
        }
    }
}
