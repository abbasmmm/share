Imports System
Imports System.Collections.Concurrent
Imports System.Linq
Imports System.Threading

Public Class AutoExpiringCache(Of TKey, TValue)
    Private Class CacheItem
        Public ReadOnly Property Value As TValue
        Public ReadOnly Property ExpirationTime As DateTime

        Public Sub New(ByVal value As TValue, ByVal expirationTime As TimeSpan)
            Me.Value = value
            Me.ExpirationTime = DateTime.UtcNow.Add(expirationTime)
        End Sub

        Public Function IsExpired() As Boolean
            Return DateTime.UtcNow > ExpirationTime
        End Function
    End Class

    Private ReadOnly cache As New ConcurrentDictionary(Of TKey, CacheItem)()
    Private ReadOnly cleanupInterval As TimeSpan
    Private ReadOnly cleanupTimer As Timer

    Public Sub New(ByVal cleanupInterval As TimeSpan)
        Me.cleanupInterval = cleanupInterval
        cleanupTimer = New Timer(AddressOf Cleanup, Nothing, cleanupInterval, cleanupInterval)
    End Sub

    Public Sub AddOrUpdate(ByVal key As TKey, ByVal value As TValue, ByVal expirationTime As TimeSpan)
        Dim cacheItem As New CacheItem(value, expirationTime)
        cache(key) = cacheItem
    End Sub

    Public Function TryGetValue(ByVal key As TKey, ByRef value As TValue) As Boolean
        Dim cacheItem As CacheItem = Nothing

        If cache.TryGetValue(key, cacheItem) AndAlso Not cacheItem.IsExpired() Then
            value = cacheItem.Value
            Return True
        End If

        cache.TryRemove(key, Nothing)
        value = Nothing
        Return False
    End Function

    Private Sub Cleanup(ByVal state As Object)
        Dim expiredKeys = cache.Where(Function(pair) pair.Value.IsExpired()).Select(Function(pair) pair.Key).ToList()

        For Each key In expiredKeys
            cache.TryRemove(key, Nothing)
        Next
    End Sub

    Public Sub Dispose()
        cleanupTimer.Dispose()
    End Sub
End Class
