package com.chaskha.d4overlay

import android.app.Application
import com.chaskha.d4overlay.alerts.Notifications
import com.chaskha.d4overlay.alerts.RefreshWorker
import com.chaskha.d4overlay.i18n.Loc

class D4App : Application() {
    override fun onCreate() {
        super.onCreate()
        Loc.init(this)
        Notifications.createChannel(this)
        RefreshWorker.ensureScheduled(this)
    }
}
