Imports Amazon.KeyManagementService
Imports Amazon.KeyManagementService.Model
Imports System
Imports System.Configuration
Imports System.IO

Public Class AwsUtil
    Private Shared ReadOnly kmsKeyId As String = ConfigurationManager.AppSettings("kmsKey")

    Public Shared Function GenerateDataKey(ByVal keyRotationFrequencyInDays As Integer) As DataKey
        Using kmsClient As New AmazonKeyManagementServiceClient()
            Dim generateDataKeyRequest As New GenerateDataKeyRequest With {
                .KeyId = kmsKeyId,
                .KeySpec = "AES_256"
            }

            Dim dataKeyResponse = kmsClient.GenerateDataKeyAsync(generateDataKeyRequest).Result

            Return New DataKey With {
                .PlainDataKey = dataKeyResponse.Plaintext.ToArray(),
                .EncryptedDataKey = dataKeyResponse.CiphertextBlob.ToArray(),
                .CreatedAtUTC = DateTime.UtcNow,
                .ValidToDateUTC = DateTime.UtcNow.AddDays(keyRotationFrequencyInDays)
            }
        End Using
    End Function

    Public Shared Function DecryptDataKey(ByVal encryptedKey As Byte()) As Byte()
        If encryptedKey Is Nothing Then
            Return Nothing
        End If

        Using kmsClient As New AmazonKeyManagementServiceClient()
            Dim decryptRequest As New DecryptRequest With {
                .CiphertextBlob = New MemoryStream(encryptedKey)
            }

            Dim decryptResponse = kmsClient.DecryptAsync(decryptRequest).GetAwaiter().GetResult()
            Dim plainTextKey = decryptResponse.Plaintext.ToArray()

            Return plainTextKey
        End Using
    End Function
End Class
