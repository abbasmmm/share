Imports System
Imports System.Collections.Concurrent
Imports System.Configuration
Imports System.Data.SqlClient
Imports System.IO
Imports System.Security.Cryptography

Public Class DataKey
    Public Property Id As Integer
    Public Property PlainDataKey As Byte()
    Public Property EncryptedDataKey As Byte()
    Public Property CreatedAtUTC As DateTime
    Public Property ValidToDateUTC As DateTime
End Class

Public NotInheritable Class EncryptionUtil
    Private Shared ReadOnly ConnectionString As String = ConfigurationManager.ConnectionStrings("piasConnectionString").ConnectionString
    Private Shared ReadOnly TransactionDataKeyCache As New AutoExpiringCache(Of Guid, Integer)(TimeSpan.FromMinutes(10))
    Private Shared ReadOnly DataKeyCache As New ConcurrentDictionary(Of Integer, Byte())()
    Private Shared CurrentKey As DataKey
    Private Shared ReadOnly KeyRotationFrequencyInDays As Integer = Integer.Parse(ConfigurationManager.AppSettings("keyRotationFrequencyInDays"))

    Public Shared Function EncryptText(plaintext As String, transactionId As Guid) As String
        Try
            EnsureKeyLoaded()
            Dim dataKey As Byte() = GetDataKeyForTransactionId(transactionId)
            Return EncryptWithAes(plaintext, dataKey)
        Catch ex As Exception
            Console.WriteLine(ex.ToString())
            Console.WriteLine(ex.StackTrace)
            Throw
        End Try
    End Function

    Private Shared Function GetDataKeyForTransactionId(transactionId As Guid) As Byte()
        Dim dataKey As Byte()
        Dim keyId As Integer

        If TransactionDataKeyCache.TryGetValue(transactionId, keyId) Then
            dataKey = DataKeyCache(keyId)
        Else
            keyId = RetrieveKeyIdForTransactionFromDatabase(transactionId)

            If keyId <> 0 Then
                If Not DataKeyCache.TryGetValue(keyId, dataKey) Then
                    InitialiseCacheForKeyId(keyId, dataKey)
                End If
            Else
                keyId = CurrentKey.Id
                dataKey = CurrentKey.PlainDataKey
                SaveTransactionKeyMappingToDatabase(transactionId, CurrentKey.Id)
            End If

            TransactionDataKeyCache.AddOrUpdate(transactionId, keyId, TimeSpan.FromDays(1))
        End If

        Return dataKey
    End Function

    Private Shared baton As New Object()

    Public Shared Sub EnsureKeyLoaded()
        If CurrentKey Is Nothing OrElse CurrentKey.ValidToDateUTC < DateTime.UtcNow Then
            SyncLock baton
                CurrentKey = RetrieveActiveKeyFromDatabase(KeyRotationFrequencyInDays)

                If CurrentKey Is Nothing Then
                    CurrentKey = AwsUtil.GenerateDataKey(KeyRotationFrequencyInDays)
                    SaveDataKeyInDatabase(CurrentKey)
                End If

                DataKeyCache(CurrentKey.Id) = CurrentKey.PlainDataKey
            End SyncLock
        End If
    End Sub

    Private Shared Sub SaveDataKeyInDatabase(dataKey As DataKey)
        Const insertQuery As String = "INSERT INTO EncryptedDataKey (EncryptedKey, CreatedAtUTC) OUTPUT INSERTED.EncryptedDataKeyId VALUES (@EncryptedKey, @CreatedAtUTC)"

        Using connection As New SqlConnection(ConnectionString)
            connection.Open()
            Using cmd As New SqlCommand(insertQuery, connection)
                cmd.Parameters.AddWithValue("@EncryptedKey", dataKey.EncryptedDataKey)
                cmd.Parameters.AddWithValue("@CreatedAtUTC", dataKey.CreatedAtUTC)

                dataKey.Id = CInt(cmd.ExecuteScalar())
            End Using
        End Using
    End Sub

    Private Shared Function RetrieveActiveKeyFromDatabase(keyRotationFrequencyInDays As Integer) As DataKey
        Using connection As New SqlConnection(ConnectionString)
            connection.Open()
            Using cmd As New SqlCommand("SELECT TOP 1 EncryptedDataKeyId, EncryptedKey, CreatedAtUTC FROM EncryptedDataKey WHERE GETUTCDATE() < DATEADD(DAY, @KeyRotationFrequencyInDays, CreatedAtUTC) ORDER BY CreatedAtUTC DESC", connection)
                cmd.Parameters.AddWithValue("@KeyRotationFrequencyInDays", keyRotationFrequencyInDays)

                Using reader = cmd.ExecuteReader()
                    If reader.Read() Then
                        Dim encryptedKey As Byte() = CType(reader("EncryptedKey"), Byte())
                        Return New DataKey() With {
                            .Id = reader.GetInt32(0),
                            .EncryptedDataKey = encryptedKey,
                            .PlainDataKey = AwsUtil.DecryptDataKey(encryptedKey),
                            .CreatedAtUTC = reader.GetDateTime(2),
                            .ValidToDateUTC = reader.GetDateTime(2).AddDays(keyRotationFrequencyInDays)
                        }
                    End If
                End Using
            End Using
        End Using

        Return Nothing
    End Function

    Public Shared Function DecryptText(encryptedText As String, transactionId As Guid) As String
        Try
            Dim keyId As Integer
            If Not TransactionDataKeyCache.TryGetValue(transactionId, keyId) Then
                keyId = RetrieveKeyIdForTransactionFromDatabase(transactionId)
            End If

            Dim plainDataKey As Byte()
            If Not DataKeyCache.TryGetValue(keyId, plainDataKey) Then
                InitialiseCacheForKeyId(keyId, plainDataKey)
            End If

            Return DecryptWithAes(encryptedText, plainDataKey)
        Catch ex As Exception
            Console.WriteLine(ex.ToString())
            Console.WriteLine(ex.StackTrace)
            Throw
        End Try
    End Function

    Private Shared Sub SaveTransactionKeyMappingToDatabase(transactionId As Guid, keyId As Integer)
        Using connection As New SqlConnection(ConnectionString)
            connection.Open()
            Using cmd As New SqlCommand("INSERT INTO TransactionDataKeyMapping (TransactionId, DataKeyId) VALUES (@TransactionId, @DataKeyId)", connection)
                cmd.Parameters.AddWithValue("@TransactionId", transactionId)
                cmd.Parameters.AddWithValue("@DataKeyId", keyId)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Private Shared Function RetrieveKeyIdForTransactionFromDatabase(transactionId As Guid) As Integer
        Console.WriteLine("Getting the key from db for transaction id: " & transactionId.ToString())
        Using connection As New SqlConnection(ConnectionString)
            connection.Open()
            Using cmd As New SqlCommand("SELECT DataKeyId FROM TransactionDataKeyMapping WHERE TransactionId = @TransactionId", connection)
                cmd.Parameters.AddWithValue("@TransactionId", transactionId)
                Dim result = cmd.ExecuteScalar()
                Return If(result Is Nothing, 0, CInt(result))
            End Using
        End Using
    End Function

    Private Shared Sub InitialiseCacheForKeyId(keyId As Integer, ByRef dataKey As Byte())
        Using connection As New SqlConnection(ConnectionString)
            connection.Open()
            Using cmd As New SqlCommand("SELECT EncryptedKey FROM EncryptedDataKey WHERE EncryptedDataKeyId = @KeyId", connection)
                cmd.Parameters.AddWithValue("@KeyId", keyId)
                Dim encryptedKey As Byte() = CType(cmd.ExecuteScalar(), Byte())
                DataKeyCache(keyId) = AwsUtil.DecryptDataKey(encryptedKey)
                dataKey = DataKeyCache(keyId)
            End Using
        End Using
    End Sub

    Private Shared Function EncryptWithAes(plainText As String, key As Byte()) As String
        Using aesAlg = Aes.Create()
            aesAlg.Key = key
            aesAlg.GenerateIV()

            Using encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV)
                Using msEncrypt As New MemoryStream()
                    msEncrypt.Write(aesAlg.IV, 0, aesAlg.IV.Length)
                    Using csEncrypt As New CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write)
                        Using swEncrypt As New StreamWriter(csEncrypt)
                            swEncrypt.Write(plainText)
                        End Using
                    End Using
                    Return Convert.ToBase64String(msEncrypt.ToArray())
                End Using
            End Using
        End Using
    End Function

    Private Shared Function DecryptWithAes(encryptedText As String, key As Byte()) As String
        Dim encryptedBytes = Convert.FromBase64String(encryptedText)
        Using aesAlg = Aes.Create()
            Using msDecrypt As New MemoryStream(encryptedBytes)
                Dim iv(15) As Byte
                msDecrypt.Read(iv, 0, iv.Length)
                aesAlg.Key = key
                aesAlg.IV = iv

                Using decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV)
                    Using csDecrypt As New CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read)
                        Using srDecrypt As New StreamReader(csDecrypt)
                            Return srDecrypt.ReadToEnd()
                        End Using
                    End Using
                End Using
            End Using
        End Using
    End Function
End Class
