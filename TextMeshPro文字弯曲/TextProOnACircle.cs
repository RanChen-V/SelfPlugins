//MIT许可证

//版权所有(c) 2019 Antony Vitillo(又名 "Skarredghost")

//特此免费授予任何获得本软件副本和相关文档文件（"软件"）的人
//在软件中不受限制地处理，包括但不限于使用、复制、修改、合并、发布、分发、再许可和/或销售
//软件的副本，并允许向其提供软件的人这样做，但须符合以下条件：

//上述版权声明和本许可声明应包含在软件的所有副本或主要部分中。

//本软件按"原样"提供，不提供任何明示或暗示的保证，包括但不限于对适销性、
//特定用途适用性和非侵权性的保证。在任何情况下，作者或版权持有人均不对任何索赔、损害或其他
//责任承担责任，无论是在合同诉讼、侵权诉讼或其他诉讼中，由软件或软件的使用或其他交易引起、
//产生或与之相关。

using UnityEngine;
using System.Collections;
using System.Runtime.CompilerServices;
using TMPro;
using Unity.Collections;

namespace ntw.CurvedTextMeshPro
{
    /// <summary>
    /// 用于在圆弧上绘制Text Pro文本的类
    /// </summary>
    [ExecuteInEditMode]
    public class TextProOnACircle : TextProOnACurve
    {
        /// <summary>
        /// 文本圆弧的半径
        /// </summary>
        [SerializeField]
        [Tooltip("文本圆弧的半径")]
        private float m_radius = 10.0f;

        /// <summary>
        /// 文本弧应该跨越多少度
        /// </summary>
        [SerializeField]
        [Tooltip("文本弧应该跨越多少度")]
        private float m_arcDegrees = 90.0f;

        /// <summary>
        /// 弧应该居中的角度偏移，以度为单位。
        /// -90度意味着文本在最高点居中
        /// </summary>
        [SerializeField]
        [Tooltip("弧应该居中的角度偏移，以度为单位")]
        private float m_angularOffset = -90;

        /// <summary>
        /// 每个字母的最大度数应该是多少。例如，如果您指定
        /// 10度，字母之间的距离永远不会超过10度。
        /// 这对于创建优雅地展开直到达到完整弧的文本很有用，
        /// 当字符串较短时不会使字母过于稀疏
        /// </summary>
        [Tooltip("字母之间的最大角度距离，以度为单位")]
        private int m_maxDegreesPerLetter = 360;

        /// <summary>
        /// <see cref="m_radius"/>的前一个值
        /// </summary>
        private float m_oldRadius = float.MaxValue;

        /// <summary>
        /// <see cref="m_arcDegrees"/>的前一个值
        /// </summary>
        private float m_oldArcDegrees = float.MaxValue;

        /// <summary>
        /// <see cref="m_angularOffset"/>的前一个值
        /// </summary>
        private float m_oldAngularOffset = float.MaxValue;

        /// <summary>
        /// <see cref="m_maxDegreesPerLetter"/>的前一个值
        /// </summary>
        private float m_oldMaxDegreesPerLetter = float.MaxValue;

        /// <summary>
        /// 每帧执行的方法，检查某些参数是否已更改
        /// </summary>
        /// <returns></returns>
        protected override bool ParametersHaveChanged()
        {
            
            //检查参数是否已更改并更新旧值以供下一帧迭代使用 废弃判断:|| m_oldMaxDegreesPerLetter != m_maxDegreesPerLetter
            bool retVal = m_radius != m_oldRadius || m_arcDegrees != m_oldArcDegrees || m_angularOffset != m_oldAngularOffset;

            m_oldRadius = m_radius;
            m_oldArcDegrees = m_arcDegrees;
            m_oldAngularOffset = m_angularOffset;
            m_oldMaxDegreesPerLetter = m_maxDegreesPerLetter;

            return retVal;
        }

        /// <summary>
        /// 计算变换矩阵，该矩阵将每个单个字符的顶点从字符中心到顶点的最终目标位置的偏移量映射，
        /// 使文本遵循曲线
        /// </summary>
        /// <param name="charMidBaselinePos">字符中心点的位置</param>
        /// <param name="zeroToOnePos">字符相对于框边界的水平位置，范围[0, 1]</param>
        /// <param name="textInfo">我们正在显示的文本信息</param>
        /// <param name="charIdx">我们必须计算变换的字符索引</param>
        /// <returns>要应用于文本所有顶点的变换矩阵</returns>
        protected override Matrix4x4 ComputeTransformationMatrix(Vector3 charMidBaselinePos, float zeroToOnePos, TMP_TextInfo textInfo, int charIdx)
        {
            if (m_arcDegrees == 0)
            {
                return Matrix4x4.zero;
            }
            
            var rectTrans = textInfo.textComponent.rectTransform;

            // float totalHeight = 0;
            // float curLineHeight = 0;
             var lineIndex = textInfo.characterInfo[charIdx].lineNumber;
            // for (var i = 1; i < textInfo.lineInfo.Length; i++)
            // {
            //     var lineInfo = textInfo.lineInfo[i];
            //     if (lineInfo.characterCount <= 0) break;
            //     totalHeight += lineInfo.lineHeight;
            //     if (i <= lineIndex) curLineHeight += lineInfo.lineHeight;
            // }

            float boundsMaxX = textInfo.lineInfo[lineIndex].lineExtents.max.x;
            float boundsMinX = textInfo.lineInfo[lineIndex].lineExtents.min.x;
            
            zeroToOnePos = (charMidBaselinePos.x - boundsMinX) / (boundsMaxX - boundsMinX);
            
            float lastPrecent = textInfo.lineInfo[lineIndex].length / rectTrans.rect.width;//计算每行长度实际占用的百分比
            if (lastPrecent > 1) lastPrecent = 1;
            float actualArcDegrees = lastPrecent * m_arcDegrees;
            // m_maxDegreesPerLetter = (int)(actualArcDegrees / ((float)textInfo.characterCount / textInfo.lineCount));
            //计算考虑字母之间最大距离的弧的实际度数
            //float actualArcDegrees = Mathf.Min(m_arcDegrees, (float)textInfo.characterCount / textInfo.lineCount * m_maxDegreesPerLetter);

            //计算显示此字符的角度。
            //我们希望字符串在圆的最高点居中，所以我们首先将位置从范围[0, 1]转换为[-0.5, 0.5]，
            //然后添加m_angularOffset度，使其在所需点居中
            float angle = ((zeroToOnePos - 0.5f) * actualArcDegrees + m_angularOffset) * Mathf.Deg2Rad; //我们需要弧度用于sin和cos
            
            //计算字符中心点新位置的坐标。使用sin和cos，因为我们在圆上。
            //注意，我们必须进行一些额外的计算，因为我们必须考虑到文本可能在多行上
            float x0 = Mathf.Cos(angle);
            float y0 = Mathf.Sin(angle);//(textInfo.lineInfo[0].lineExtents.max.y - textInfo.lineInfo[0].lineExtents.min.y)
            float radiusForThisLine =
                m_radius + textInfo.characterInfo[charIdx].baseLine;
            // Vector2 newMideBaselinePos = new Vector2(x0 * radiusForThisLine,
            //     -y0 * radiusForThisLine - Mathf.Abs(m_radius * Mathf.Cos(actualArcDegrees*Mathf.Deg2Rad)) + textInfo.textComponent.rectTransform.rect.height/2); //字符的实际新位置

            Vector2 newMideBaselinePos = new Vector2(
                x0 * radiusForThisLine - (m_radius) * Mathf.Cos(m_angularOffset * Mathf.Deg2Rad),
                -y0 * radiusForThisLine - (m_radius) * Mathf.Abs(Mathf.Sin(m_angularOffset * Mathf.Deg2Rad))); //字符的实际新位置
            
            //计算变换矩阵：将点移动到刚找到的位置，然后旋转字符以适合曲线的角度
            //(-90是因为文本已经是垂直的，就好像它已经旋转了90度一样)
            return Matrix4x4.TRS(new Vector3(newMideBaselinePos.x, newMideBaselinePos.y, 0), Quaternion.AngleAxis(-Mathf.Atan2(y0, x0) * Mathf.Rad2Deg - 90, Vector3.forward), Vector3.one);
        }

        protected override void Calculate(TMP_TextInfo textInfo)
        {
            var lineLength = textInfo.lineInfo[0].width;
            var deg = m_arcDegrees * Mathf.Deg2Rad;
            m_radius = lineLength / deg;
        }
    }
}
