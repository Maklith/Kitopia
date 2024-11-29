namespace Core.Window;
public class ColorSpaceCtr
{
    private static float[,] MCAT02 =
    { 
        {0.7328f, 0.4296f, -0.1624f},
        {-0.7036f, 1.6975f, 0.0061f},
        {0.0030f, 0.0136f, 0.9834f}
    };
    public static float[,] CtrColorSpace(float[] src, float[] dst)
    {
       
        //r r g g b b w w
        
        var srcWhite = tristimulus( src[6..8] );
        
        var dstWhite = tristimulus(dst[6..8]);

        var srcNpm = Npm(new float[,] {
            { src[0],src[1] }, // R
            { src[2],src[3] }, // G
            { src[4],src[5] }  // B
        }, srcWhite);
        var dstNpm = Npm(new  float[,]
        {
            { dst[0],dst[1] }, // R
            { dst[2],dst[3] }, // G
            { dst[4],dst[5]}  // B
        }, dstWhite);

        float[,] adaptedXYZ;
        float[,] xyz2rgb;
        float[,] convMat;

        // 模型选择：无适配模型
        {
            // 适配模型计算
            var aMat = AdaptMat(MCAT02, srcWhite, dstWhite);
            adaptedXYZ = Mul33(aMat, srcNpm);
            xyz2rgb = InvertMatrix(dstNpm);
        }

        // 计算转换矩阵
        convMat = Mul33(xyz2rgb, adaptedXYZ);
        return convMat;
    }
    static float[,] AdaptMat(float[,] mat, float[] source, float[] target)
    {
        // 计算源和目标白点在当前矩阵下的结果
        float[] w1 = Mul3(mat, source);
        float[] w2 = Mul3(mat, target);

        // 计算比例因子
        float[] q = { w2[0] / w1[0], w2[1] / w1[1], w2[2] / w1[2] };

        // 生成对角缩放矩阵
        float[,] m = Mul3Col(InvertMatrix(mat), q);

        // 计算适配矩阵
        return Mul33(m, mat);
    }
    static float[,] InvertMatrix(float[,] matrix)
    {
        // 矩阵求逆，仅支持 3x3 矩阵
        float det = matrix[0, 0] * (matrix[1, 1] * matrix[2, 2] - matrix[1, 2] * matrix[2, 1])
                     - matrix[0, 1] * (matrix[1, 0] * matrix[2, 2] - matrix[1, 2] * matrix[2, 0])
                     + matrix[0, 2] * (matrix[1, 0] * matrix[2, 1] - matrix[1, 1] * matrix[2, 0]);

        if (Math.Abs(det) < 1E-15)
            throw new InvalidOperationException("Matrix is singular and cannot be inverted.");

        float invDet = (float)(1.0 / det);

        float[,] result = new float[3, 3];
        result[0, 0] = (matrix[1, 1] * matrix[2, 2] - matrix[1, 2] * matrix[2, 1]) * invDet;
        result[0, 1] = (matrix[0, 2] * matrix[2, 1] - matrix[0, 1] * matrix[2, 2]) * invDet;
        result[0, 2] = (matrix[0, 1] * matrix[1, 2] - matrix[0, 2] * matrix[1, 1]) * invDet;
        result[1, 0] = (matrix[1, 2] * matrix[2, 0] - matrix[1, 0] * matrix[2, 2]) * invDet;
        result[1, 1] = (matrix[0, 0] * matrix[2, 2] - matrix[0, 2] * matrix[2, 0]) * invDet;
        result[1, 2] = (matrix[0, 2] * matrix[1, 0] - matrix[0, 0] * matrix[1, 2]) * invDet;
        result[2, 0] = (matrix[1, 0] * matrix[2, 1] - matrix[1, 1] * matrix[2, 0]) * invDet;
        result[2, 1] = (matrix[0, 1] * matrix[2, 0] - matrix[0, 0] * matrix[2, 1]) * invDet;
        result[2, 2] = (matrix[0, 0] * matrix[1, 1] - matrix[0, 1] * matrix[1, 0]) * invDet;

        return result;
    }

    static void PrintMatrix(float[,] matrix)
    {
        for (int i = 0; i < matrix.GetLength(0); i++)
        {
            for (int j = 0; j < matrix.GetLength(1); j++)
            {
                Console.Write($"{matrix[i, j]:F6} ");
            }
            Console.WriteLine();
        }
    }
    static float[] Mul3(float[,] M, float[] a)
    {
        // 矩阵与列向量相乘 (M * a)
        return new float[]
        {
            M[0, 0] * a[0] + M[0, 1] * a[1] + M[0, 2] * a[2],
            M[1, 0] * a[0] + M[1, 1] * a[1] + M[1, 2] * a[2],
            M[2, 0] * a[0] + M[2, 1] * a[1] + M[2, 2] * a[2]
        };
    }

    static float[] Mul3T(float[,] M, float[] a)
    {
        // 矩阵转置后与列向量相乘 (M' * a)
        return new float[]
        {
            M[0, 0] * a[0] + M[1, 0] * a[1] + M[2, 0] * a[2],
            M[0, 1] * a[0] + M[1, 1] * a[1] + M[2, 1] * a[2],
            M[0, 2] * a[0] + M[1, 2] * a[1] + M[2, 2] * a[2]
        };
    }

    static float[,] Mul33(float[,] A, float[,] B)
    {
        // 矩阵相乘 (A * B)
        float[,] result = new float[3, 3];
        for (int i = 0; i < 3; i++)
        {
            float[] row = { A[i, 0], A[i, 1], A[i, 2] };
            result[i, 0] = Mul3T(B, row)[0];
            result[i, 1] = Mul3T(B, row)[1];
            result[i, 2] = Mul3T(B, row)[2];
        }
        return result;
    }

    static float[,] Mul3Col(float[,] M, float[] q)
    {
        // 对矩阵 M 的列进行缩放，列向量分别乘以 q 的分量
        float[,] result = new float[3, 3];
        for (int i = 0; i < 3; i++)
        {
            result[i, 0] = M[i, 0] * q[0];
            result[i, 1] = M[i, 1] * q[1];
            result[i, 2] = M[i, 2] * q[2];
        }
        return result;
    }
  

    private static float[] tristimulus(float[] xy,float Y=1) {
        var z = 1 - xy[0] - xy[1];
        return [Y * xy[0] / xy[1], Y, Y * z / xy[1]];
    }
    private static float[,] Npm(float[,] Mp, float[] ctr)
    {
        // 提取 x、y 和 z
        float[] x = { Mp[0, 0], Mp[1, 0], Mp[2, 0] };
        float[] y = { Mp[0, 1], Mp[1, 1], Mp[2, 1] };
        float[] z = {
            1 - x[0] - y[0],
            1 - x[1] - y[1],
            1 - x[2] - y[2]
        };

        // 求解线性方程组
        float[] d = Solve3(new float[][] { x, y, z }, ctr);

        // 生成结果矩阵
        float[,] result = new float[3, 3];
        for (int i = 0; i < 3; i++)
        {
            result[0, i] = x[i] * d[i];
            result[1, i] = y[i] * d[i];
            result[2, i] = z[i] * d[i];
        }

        return result;
    }
    static float[] Solve3(float[][] M, float[] a)
    {
        float d = Det3(M); // 原矩阵的行列式

        if (Math.Abs(d) <= 1E-15)
        {
            throw new Exception("Matrix is singular");
        }

        d = (float)(1.0 / d);

        // 计算 d1, d2, d3
        float d1 = Det3(ReplaceColumn(M, a, 0));
        float d2 = Det3(ReplaceColumn(M, a, 1));
        float d3 = Det3(ReplaceColumn(M, a, 2));

        // 返回结果
        return new float[] { d1 * d, d2 * d, d3 * d };
    }
   
    static float Det3(float[][] M)
    {
        // 计算 3x3 矩阵的行列式
        return M[0][0] * (M[1][1] * M[2][2] - M[1][2] * M[2][1])
               - M[0][1] * (M[1][0] * M[2][2] - M[1][2] * M[2][0])
               + M[0][2] * (M[1][0] * M[2][1] - M[1][1] * M[2][0]);
    }

    static float[][] ReplaceColumn(float[][] M, float[] column, int colIndex)
    {
        // 替换矩阵 M 的第 colIndex 列为向量 column
        float[][] result = new float[3][];
        for (int i = 0; i < 3; i++)
        {
            result[i] = (float[])M[i].Clone();
            result[i][colIndex] = column[i];
        }

        return result;
    }
}