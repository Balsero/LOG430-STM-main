# Laboratoire #2

Groupe: 2 
Equipe: 04

Membres de l'équipe:
Jonathan Rodriguez Tames 

## Évaluation de la participation

>L'évaluation suivante est faite afin d'encourager des discussions au sein de l'équipe. Une discussion saine du travail de chacun est utile afin d'améliorer le climat de travail. Les membres de l'équipe ont le droit de retirer le nom d'un ou une collègue du rapport.

| nom de l'étudiant    | Facteur multiplicatif |
|:--------------------:|:--------------------:|
| Jean Travaillant      |          1           |
| Joe Paresseux         |        0.75          |
| Jules Procrastinateu  |        0.5           |
| Jeanne Parasite       |        0.25          |
| Jay Oublié            |          0           |

## Introduction

>TODO: insérer votre introduction

## Vues architecturales

- Au travers des différentes vues architecturales, montrez comment la redondance est présente dans vos microservices après l'implémentation du laboratoire 2. La présence des vues primaires et des catalogues d'éléments est nécessaire. Assurez-vous de bien présenter la tactique de redondance dans vos vues.

### Vues architecturales de type module - redondance

### Vues architecturales de type composant et connecteur - redondance

### Vues architecturales de type allocation - redondance

>Note : Une légende est nécessaire dans les vues primaires pour assurer une compréhension claire des éléments représentés.

## Alternatives envisagées

Dans cette expérience, nous avions le choix d’implémenter deux types de redondances différentes pour TripComparator : soit une redondance passive pour l'assignation du Leader, soit une redondance active pour l’envoi des messages de chaque TripComparator. Dans tous les cas, chaque implémentation avait ses particularités, ses avantages et ses inconvénients, que nous présenterons ici. Par la suite, nous expliquerons pourquoi nous avons choisi une approche plutôt qu’une autre.

Redondance Passive
La redondance passive pour le TripComparator présente certaines particularités. Tout d’abord, il n’y a qu’un seul TripComparator Leader qui construit le message à envoyer à RabbitMQ. Pendant ce temps, l’autre instance reste en veille (stand-by) jusqu’à ce que le Leader cesse de fonctionner. À ce moment-là, l’instance en veille prend la relève.

Pour permettre ce fonctionnement, nous avons implémenté un service Manager, dont le rôle principal est de surveiller en permanence l’état des deux instances de TripComparator. Le Manager doit également s’assurer qu’il n’y a qu’un seul Leader actif à tout moment, car plusieurs Leaders pourraient entraîner des messages dupliqués dans l’affichage.

De plus, comme une instance doit reprendre l’exécution après la défaillance d’un Leader, il est crucial que le nouveau Leader sache où en est le système dans l’exécution des tâches. Pour cela, nous avons intégré une base de données Redis. Redis stocke les informations initiales de l’expérience ainsi que l’état du système en fonction des tâches accomplies. Ainsi, le nouveau Leader peut reprendre précisément là où s’était arrêté son prédécesseur, évitant les répétitions de tâches qui pourraient provoquer des erreurs dans les messages affichés.

Redondance Active
En ce qui concerne la redondance active, cette approche présente des particularités distinctes. Dans ce mode, les deux instances de TripComparator envoient simultanément des messages à RabbitMQ. Cela nécessite un filtre pour sélectionner un seul message à afficher sur le tableau de bord.

Cependant, chaque instance effectue des requêtes en continu auprès du STM, rendant le filtrage plus complexe. Bien que nous ayons envisagé d’implémenter un filtre pour nettoyer les messages, nous avons finalement abandonné cette solution. La principale raison était notre compréhension insuffisante du processus d’acheminement des messages vers RabbitMQ et des mécanismes nécessaires pour concevoir un filtre efficace.

Décision Finale
Nous avons finalement opté pour la redondance passive, car cette approche avait déjà été implémentée pour le service STM. Cela nous a permis de réutiliser les SideCars existants pour communiquer avec le Manager concernant l’état du service STM, ce qui nous a fait gagner un temps précieux.

De plus, nous avons pu capitaliser sur nos connaissances de Redis pour stocker les données essentielles au bon déroulement de l’expérience. Cette solution s’est avérée plus rapide à mettre en œuvre et mieux adaptée à notre contexte technique, tout en évitant les complexités liées à la redondance active.
Alex — 10/11/2024 23:40
L'interopérabilité est la capacité de différents systèmes, applications ou services à communiquer, échanger des données et travailler ensemble de manière efficace, même s'ils sont construits sur des technologies ou des plateformes différentes. Dans le contexte des microservices, cela signifie que chaque service doit pouvoir interagir avec d'autres services, peu importe la manière dont ils sont développés, déployés ou structurés, tout en garantissant une intégration fluide et cohérente.


## Diagrammes de séquence pour expliquer le fonctionnement des tactiques de redondance

- Vous devez fournir les diagrammes de séquence démontrant le fonctionnement de l'architecture avant le début du laboratoire 2.
- Vous devez fournir les diagrammes de séquence démontrant le fonctionnement après la réalisation du laboratoire 2.
- Représentez les différents moments de vie du système, si applicables:
  - L'état au démarrage des conteneurs,
  - La mécanique de détection d'un problème,
  - La mécanique de rétablissement du service, de récupération ou de reconfiguration.

## Questions

- **Question 1** Discuter de l'interopérabilité des microservices, tout en fournissant des exemples d'une ou plusieurs tactiques utilisées. Si vous pensez qu'aucune tactique n'est utilisée dans le code, nommez et décrivez une ou plusieurs tactiques qui pourraient être utilisées.

- **Question 2** Qu'est-ce que l'injection de dépendance? Fournissez des exemples de l'utilisation de l'injection de dépendance dans un des microservices du projet. Comment l'injection de dépendance améliore-t-elle la testabilité du système?

- **Question 3** : Dans le laboratoire, nous utilisons une technologie semblable à Kubernetes. Qu'est-ce que Kubernetes et en quoi cette technologie est-elle pertinente à la redondance ? Quels sont les éléments et les concepts de Kubernetes qui sont présents dans le projet du laboratoire ?

# Conclusion

>TODO: insérer votre conclusion

- N'oubliez pas d'effacer les TODO.
- Générer une version PDF de ce document pour votre remise finale.
- Assurez-vous du bon format de votre rapport PDF.
- Créer un tag git avec la commande "git tag laboratoire-2".

\newpage

# Annexes
