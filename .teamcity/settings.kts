import jetbrains.buildServer.configs.kotlin.*
import jetbrains.buildServer.configs.kotlin.buildSteps.script
import jetbrains.buildServer.configs.kotlin.triggers.vcs

version = "2025.11"

project {
    buildType {
        id("BuildAndTest")
        name = "Build and smoke tests"
        artifactRules = "tests/.output/*.log => api-logs"

        vcs {
            root(DslContext.settingsRoot)
        }

        steps {
            script {
                name = "Build and run isolated smoke tests"
                scriptContent = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\\ci.ps1"
            }
        }

        triggers {
            vcs { }
        }

        requirements {
            contains("teamcity.agent.jvm.os.name", "Windows")
        }
    }
}
