package app.clipbridge.updates
import android.app.job.JobParameters
import android.app.job.JobService
class UpdateJobService : JobService() {
    override fun onStartJob(params: JobParameters): Boolean {
        val repo=UpdateRepository.get(this)
        if(!repo.autoCheck()) return false
        repo.check(force=true) { retry -> jobFinished(params,retry) }; return true
    }
    override fun onStopJob(params: JobParameters): Boolean { UpdateRepository.get(this).cancel(); return true }
}
