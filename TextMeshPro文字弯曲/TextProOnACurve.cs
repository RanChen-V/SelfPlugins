//MIT 许可证

//版权所有(c) 2019 Antony Vitillo(又名 "Skarredghost")

//特此免费授予任何获得本软件副本和相关文档文件（"软件"）的人，
//不受限制地处理本软件，包括但不限于使用、复制、修改、合并、发布、
//分发、再许可和/或销售软件副本的权利，并允许向其提供软件的人这样做，
//但须符合以下条件：

//上述版权声明和本许可声明应包含在软件的所有副本或主要部分中。

//本软件按"原样"提供，不提供任何明示或暗示的保证，包括但不限于
//对适销性、特定用途适用性和非侵权性的保证。在任何情况下，作者或
//版权持有人均不对因软件或软件使用或其他交易而产生的任何索赔、损害
//或其他责任承担责任，无论是在合同诉讼、侵权诉讼或其他诉讼中。

//代码灵感来源于TextMeshPro包中的WarpTextExample

using UnityEngine;
using System.Collections;
using TMPro;
using System;

namespace ntw.CurvedTextMeshPro
{
    /// <summary>
    /// 用于绘制沿特定曲线排列的Text Pro文本的基类
    /// </summary>
    [ExecuteInEditMode]
    public abstract class TextProOnACurve : MonoBehaviour
    {
        /// <summary>
        /// 感兴趣的文本组件
        /// </summary>
        private TMP_Text m_TextComponent;

        /// <summary>
        /// 如果文本必须在此帧更新则为真
        /// </summary>
        private bool m_forceUpdate;

        /// <summary>
        /// Awake
        /// </summary>
        private void Awake()
        {
            m_TextComponent = gameObject.GetComponent<TMP_Text>();
        }

        /// <summary>
        /// OnEnable
        /// </summary>
        private void OnEnable()
        {
            //每次对象被启用时，我们必须强制重新创建文本网格
            m_forceUpdate = true;
        }

        /// <summary>
        /// Update
        /// </summary>
        protected void Update()
        {
            //如果文本和参数与上一帧相同，不要浪费时间重新计算所有内容
            if (!m_forceUpdate && !m_TextComponent.havePropertiesChanged && !ParametersHaveChanged())
            {
                return;
            }

            m_forceUpdate = false;

            //在循环中，vertices表示我们正在分析的单个字符的4个顶点，
            //而matrix是旋转平移矩阵，它将旋转和缩放字符，使它们能够跟随曲线
            Vector3[] vertices;
            Matrix4x4 matrix;

            //生成网格并获取关于文本和字符的信息
            m_TextComponent.ForceMeshUpdate();

            TMP_TextInfo textInfo = m_TextComponent.textInfo;
            int characterCount = textInfo.characterCount;

            //如果字符串为空，无需浪费时间
            if (characterCount == 0)
                return;

            Calculate(textInfo);
            //获取包含文本的矩形的边界
            float boundsMinX = m_TextComponent.bounds.min.x;
            float boundsMaxX = m_TextComponent.bounds.max.x;

            //对于每个字符
            for (int i = 0; i < characterCount; i++)
            {
                //跳过不可见的字符
                if (!textInfo.characterInfo[i].isVisible)
                    continue;

                //获取此字符使用的网格索引，然后是材质的索引...并使用所有这些数据获取
                //包围此字符的矩形的4个顶点。将它们存储在vertices中
                int vertexIndex = textInfo.characterInfo[i].vertexIndex;
                int materialIndex = textInfo.characterInfo[i].materialReferenceIndex;
                vertices = textInfo.meshInfo[materialIndex].vertices;

                //计算每个字符的基线中点。这是字符的中心点。
                //我们将使用它作为表示此字符的几何变换点
                Vector3 charMidBaselinePos = new Vector2((vertices[vertexIndex + 0].x + vertices[vertexIndex + 2].x) / 2, textInfo.characterInfo[i].baseLine);

                //从顶点位置中移除中心点。在此操作之后，四个顶点中的每一个将只具有相对于中心位置的偏移坐标。这在处理旋转时会很有用
                vertices[vertexIndex + 0] += -charMidBaselinePos;
                vertices[vertexIndex + 1] += -charMidBaselinePos;
                vertices[vertexIndex + 2] += -charMidBaselinePos;
                vertices[vertexIndex + 3] += -charMidBaselinePos;

                // //计算字符相对于框边界的水平位置，范围在[0, 1]内，
                // //其中0是文本的左边界，1是右边界
                // 因为存在多行的情况 需要根据当前字符所在行进行计算 将代码移入矩阵计算公式
                // float zeroToOnePos = (charMidBaselinePos.x - boundsMinX) / (boundsMaxX - boundsMinX);

                //获取变换矩阵，该矩阵将顶点（视为相对于字符中心点的偏移）映射到它们的最终位置，
                //使文本能够跟随曲线
                matrix = ComputeTransformationMatrix(charMidBaselinePos, 0, textInfo, i);

                if (matrix != Matrix4x4.zero)
                {
                    //应用变换，并获得表示此字符的4个顶点的最终位置和方向
                    vertices[vertexIndex + 0] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 0]);
                    vertices[vertexIndex + 1] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 1]);
                    vertices[vertexIndex + 2] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 2]);
                    vertices[vertexIndex + 3] = matrix.MultiplyPoint3x4(vertices[vertexIndex + 3]);
                }
                else
                {
                    vertices[vertexIndex + 0] += charMidBaselinePos;
                    vertices[vertexIndex + 1] += charMidBaselinePos;
                    vertices[vertexIndex + 2] += charMidBaselinePos;
                    vertices[vertexIndex + 3] += charMidBaselinePos;
                }
            }

            //使用修订后的信息上传网格
            m_TextComponent.UpdateVertexData();
        }

        /// <summary>
        /// 在每一帧执行的方法，检查某些参数是否已更改
        /// </summary>
        /// <returns></returns>
        protected abstract bool ParametersHaveChanged();

        /// <summary>
        /// 计算变换矩阵，该矩阵将每个单个字符的顶点相对于字符中心的偏移映射到
        /// 顶点的最终目标位置，使文本能够跟随曲线
        /// </summary>
        /// <param name="charMidBaselinePosfloat">字符中心点的位置</param>
        /// <param name="zeroToOnePos">字符相对于框边界的水平位置，范围在[0, 1]内</param>
        /// <param name="textInfo">我们正在显示的文本信息</param>
        /// <param name="charIdx">我们必须为其计算变换的字符索引</param>
        /// <returns>要应用于文本所有顶点的变换矩阵</returns>
        protected abstract Matrix4x4 ComputeTransformationMatrix(Vector3 charMidBaselinePos, float zeroToOnePos, TMP_TextInfo textInfo, int charIdx);

        protected abstract void Calculate(TMP_TextInfo textInfo);
    }
}
