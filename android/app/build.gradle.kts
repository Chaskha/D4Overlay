plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Ressources partagées avec la version Windows : elles ne sont PAS recopiées dans le dépôt,
// elles sont prises directement dans Shared/ et Packaging/ à chaque build (une seule source de vérité).
val repoRoot: File = rootProject.projectDir.parentFile
val sharedAssetsDir: File = layout.buildDirectory.dir("generated/sharedAssets").get().asFile

val copySharedAssets by tasks.registering(Sync::class) {
    into(sharedAssetsDir)
    from(File(repoRoot, "Shared/Assets/Lang")) {
        include("*.json")
        into("lang")
    }
    from(File(repoRoot, "Packaging/defaults/sounds")) {
        include("*.mp3")
        into("sounds")
    }
}

android {
    namespace = "com.chaskha.d4overlay"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.chaskha.d4overlay"
        minSdk = 26
        targetSdk = 35
        versionCode = 4
        versionName = "1.0.3"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    sourceSets["main"].assets.srcDir(sharedAssetsDir)
}

tasks.named("preBuild") { dependsOn(copySharedAssets) }

// Nom du fichier produit : D4Timers-<version>-release.apk (au lieu de app-release.apk).
base { archivesName = "D4Timers-${android.defaultConfig.versionName}" }

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui-tooling-preview")
    debugImplementation("androidx.compose.ui:ui-tooling")

    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("androidx.work:work-runtime-ktx:2.10.0")
    implementation("org.jetbrains.kotlinx:kotlinx-coroutines-android:1.9.0")

    testImplementation("junit:junit:4.13.2")
    // org.json fait partie d'Android, mais n'existe pas dans les tests JVM : on prend la version Maven.
    testImplementation("org.json:json:20240303")
}
