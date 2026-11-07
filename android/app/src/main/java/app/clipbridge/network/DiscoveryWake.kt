package app.clipbridge.network

import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit

class DiscoveryWake {
    private val pending = ArrayBlockingQueue<Unit>(1)
    fun signal() { pending.offer(Unit) }
    fun await(timeoutMillis: Long): Boolean = pending.poll(timeoutMillis, TimeUnit.MILLISECONDS) != null
}